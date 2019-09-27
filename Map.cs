using System.Collections.Generic;
using UnityEngine;

public class Map : MonoBehaviour
{
    public Transform m_EpisodesParent;
    public MapCamera m_MapCamera;
    public CastleUICamera m_MapUICamera;

    public static string m_DungeonMissionToFocus;
    public static string m_MissionToFocus;
    public static MissionDifficulty m_MissionToFocusDifficulty;
    public static string m_EpisodeToFocus;
    public static bool m_NeedToShowStartSiegeDialog;
    public static bool m_NeedFindAvailableScoutMission;

    private readonly Dictionary<string, MapEpisode> m_Episodes = new Dictionary<string, MapEpisode>();
    
    public static Map Instance { get; private set; }

    ///////////////
    private void Awake()
    {
        Instance = this;
    }

    ///////////////
    private void OnDestroy()
    {
        Instance = null;
    }

    ///////////////
    public static void ClearMapFocusCache()
    {
        m_DungeonMissionToFocus = null;
        m_MissionToFocus = null;
        m_EpisodeToFocus = null;
        m_NeedFindAvailableScoutMission = false;
        m_NeedToShowStartSiegeDialog = false;
    }

    /// <summary>
    /// Start this instance.
    /// </summary>
    private void Start()
    {
        // инициализация эпизодов
        var episodes = EpisodesDataStorage.Instance.GetData();

        for (int i = 0; i < episodes.Count; i++)
        {
            var episodeData = episodes[i];

            var prefab = ResourcesHelper.GetResource<MapEpisode>(episodeData.PrefabName);

            if (prefab == null)
            {
                continue;
            }
            
            var episode = Instantiate(prefab, m_EpisodesParent, true);
            episode.Setup(episodeData);
                        
            m_Episodes.Add(episodeData.Id, episode);
        }

        // инициализация динамических объектов скаута
        if (ScoutManager.Instance.IsInited)
        {
            List<ScoutMissionInfo> scoutMissions = ScoutManager.Instance.ScoutMissions;

            for (int i = 0; i < scoutMissions.Count; i++)
            {
                ScoutMissionInfo missionInfo = scoutMissions[i];

                // неактивные и пройденные миссии не отображаем
                if (!missionInfo.IsAvailable() || missionInfo.IsFinished())
                    continue;

                DynamicObjectData dynamicObjectData =
                    DynamicObjectsDataStorage.Instance.GetById(missionInfo.MissionData.DynamicObjectId);
                SpawnData spawnData = SpawnsDataStorage.Instance.GetById(missionInfo.m_SpawnId);

                if (m_NeedFindAvailableScoutMission)
                {
                    m_MissionToFocus = missionInfo.m_Id;
                    m_EpisodeToFocus = spawnData.EpisodeId;
                }

                ScoutMapObject obj = null;

                if (missionInfo.MissionData.DynamicObjectId != null)
                    obj = Resources.Load<ScoutMapObject>(dynamicObjectData.Prefab);

                if (obj != null)
                {
                    var dynamicObject = Instantiate(Resources.Load<ScoutMapObject>(dynamicObjectData.Prefab));
                    dynamicObject.name = missionInfo.MissionData.Name;
                    dynamicObject.transform.position = new Vector3(spawnData.PositionX, spawnData.PositionY, -1);
                    dynamicObject.SetMissionInfo(missionInfo);
                }
            }
        }

        // инициализация динамических объектов бесконечных подземелий
        var dungeons = DungeonMissionsDataStorage.Instance.GetData();

        for (int i = 0; i < dungeons.Count; i++)
        {
            DungeonMissionData missionData = dungeons[i];
            // бесконечные подземелья должны быть открыты по доступности сектора.
            string missionId = missionData.RequiredCampaignMissionId;
            MissionData mission = MissionsDataStorage.Instance.GetById(missionId);
            EpisodeData episode = EpisodesDataStorage.Instance.GetById(mission.EpisodeId);
            bool available = PlayerProfile.Instance.IsMapSectorOpened(episode.SectorId);

            if (!available)
                continue;

            DynamicObjectData dynamicObjectData =
                DynamicObjectsDataStorage.Instance.GetById(missionData.DynamicObjectId);

            DungeonMapObject obj = null;

            if (missionData.DynamicObjectId != null)
                obj = Resources.Load<DungeonMapObject>(dynamicObjectData.Prefab);

            if (obj != null)
            {
                var dynamicObject = Instantiate(obj);
                dynamicObject.name = missionData.Name;
                dynamicObject.transform.position = new Vector3(missionData.PositionX, missionData.PositionY, -1);
                dynamicObject.SetMissionData(missionData);
            }
        }

        // инициализация динамических объектов мировых боссов
        if (WorldBossesManager.Instance.Missions != null)
        {
            foreach (WorldBossMissionInfo mission in WorldBossesManager.Instance.Missions)
            {
                SpawnData spawn = SpawnsDataStorage.Instance.GetById(mission.SpawnId);

                if (mission.IsFinished() || !PlayerProfile.Instance.IsMapEpisodeOpened(spawn.EpisodeId))
                    continue;

                DynamicObjectData dynamicObjectData =
                    DynamicObjectsDataStorage.Instance.GetById(mission.Data.DynamicObjectId);

                WorldBossMapObject obj = null;

                if (mission.Data.DynamicObjectId != null)
                    obj = Resources.Load<WorldBossMapObject>(dynamicObjectData.Prefab);

                if (obj != null)
                {
                    var dynamicObject = Instantiate(obj);

                    // объекты сортируются, чтобы нижние оказались поверх верних (в 2д)
                    float zPos = (spawn.PositionY / 100) - 1.9f;

                    dynamicObject.name = mission.Data.Name;
                    dynamicObject.transform.position = new Vector3(spawn.PositionX, spawn.PositionY, zPos);
                    dynamicObject.SetMissionInfo(mission);
                }
            }
        }
        
        // инициализация динамических объектов сундуков и бочек
        foreach (string spawnId in PlayerProfile.Instance.MapRewardsSpawnIds)
        {
            SpawnData spawn = SpawnsDataStorage.Instance.GetById(spawnId);

            if (spawn == null)
                continue;

            MapRewardObject mapReward = null;

            if (spawn.IsOnWater)
            {
                mapReward = Resources.Load<MapRewardObject>(PrefabName.BarrelMapRewardObject);
            }
            else
            {
                mapReward = Resources.Load<MapRewardObject>(PrefabName.ChestMapRewardObject);
            }

            if (mapReward != null)
            {
                var dynamicObject = Instantiate(mapReward);

                // объекты сортируются, чтобы нижние оказались поверх верних (в 2д)
                float zPos = (spawn.PositionY / 100) - 1.9f;

                dynamicObject.transform.position = new Vector3(spawn.PositionX, spawn.PositionY, zPos);

                dynamicObject.SetInfo(spawnId);
            }
        }

        // завершение инициалищации
        ProcessStart();
    }

    /// <summary>
    /// При необходимости фокусирует камеру и отображает нужные эпизоды
    /// </summary>
    private void ProcessStart()
    {
        m_MapCamera.SetZoom(m_MapCamera.DefaultZoom);

        // если мы нажали Повторить в конце прохождения Бесконечного подземелья или вернулись с окна тактики
        if (m_DungeonMissionToFocus != null)
        {
            DungeonMissionInfo info = DungeonsManager.Instance.GetDungeonMission(m_DungeonMissionToFocus);
            UIManager.Instance.GetDialog<EndlessDungeonDialog>().Show(info);
            
            // центрируем камеру на данже
            DungeonMissionData mission = DungeonMissionsDataStorage.Instance.GetById(m_DungeonMissionToFocus);

            if (mission != null)
            {
                m_MapCamera.SetPosition(new Vector3(mission.PositionX, mission.PositionY, 0));
            }
            else
            {
                FocusCameraLastEpisode();
            }
            return;
        }

        // если необходимо показать окно миссии, то центрируем камеру на эпизоде, привязанном к ней
        if (m_MissionToFocus != null)
        {
            var missionData = MissionsDataStorage.Instance.GetById(m_MissionToFocus);

            if (missionData != null) // если обычная миссия
            {
                var dialog = (StartMissionDialog)UIManager.Instance.GetDialog(typeof(StartMissionDialog));
                dialog.Show(missionData, m_MissionToFocusDifficulty);

                if (m_Episodes.ContainsKey(missionData.EpisodeId))
                {
                    m_MapCamera.SetPosition(m_Episodes[missionData.EpisodeId].m_Button.transform.position);
                    ClearMapFocusCache();
                }
                return;
            }

            var missionInfo = ScoutManager.Instance.GetScoutMission(m_MissionToFocus);

            if (missionInfo != null) // если миссия скаута
            {
                if (!missionInfo.IsAvailable())
                {
                    UIManager.Instance.ShowNotifyPopup(Localization.Get("scout_mission_is_over"));
                    FocusCameraLastEpisode();
                    return;
                }

                var scoutDialog = (ScoutMissionDialog)UIManager.Instance.GetDialog(typeof(ScoutMissionDialog));
                scoutDialog.Show(missionInfo);

                // центрируем камеру на скауте
                SpawnData spawnData = SpawnsDataStorage.Instance.GetById(missionInfo.m_SpawnId);

                if (spawnData != null)
                {
                    m_MapCamera.SetPosition(new Vector3(spawnData.PositionX, spawnData.PositionY));
                }
                else
                {
                    FocusCameraLastEpisode();
                }
                return;
            }

            WorldBossMissionInfo mission = WorldBossesManager.Instance.GetMissionInfo(m_MissionToFocus);

            if (mission != null)
            {
                UIManager.Instance.GetDialog<WorldBossMissionDialog>().Show(mission);
                
                // центрируем камеру на боссе
                SpawnData spawnData = SpawnsDataStorage.Instance.GetById(mission.SpawnId);

                if (spawnData != null)
                {
                    m_MapCamera.SetPosition(new Vector3(spawnData.PositionX, spawnData.PositionY));
                }
                else
                {
                    FocusCameraLastEpisode();
                }
                
                return;
            }
        }
        
        // или центрируем камеру на эпизоде при необходимости
        if (m_EpisodeToFocus != null)
        {
            var episode = m_Episodes[m_EpisodeToFocus];
            m_MapCamera.SetPosition(episode.m_EpisodeCenter);
            
            // если необходимо показать окно осады эпизода
            if (m_NeedToShowStartSiegeDialog)
            {
                EpisodeData episodeData = EpisodesDataStorage.Instance.GetById(m_EpisodeToFocus);
                UIManager.Instance.GetDialog<StartSiegeDialog>().Show(episodeData);
                m_NeedToShowStartSiegeDialog = false;
            }
            
            ClearMapFocusCache();
            return;
        }

        FocusCameraLastEpisode();
    }

    ///////////////
    private void FocusCameraLastEpisode()
    {
        // пробуем центрировать камеру на последнем незавершенном эпизоде
        EpisodeData episodeToFocus = PlayerProfile.Instance.GetLastNonCompletedEpisode();

        // если не находим, то ищем последний эпизод кампании
        if (episodeToFocus == null)
        {
            episodeToFocus = PlayerProfile.Instance.GetLastEpisode();
        }

        //центрируем на последнем непройденном эпизоде
        MapEpisode mapEpisode = GetEpisode(episodeToFocus.Id);
        m_MapCamera.SetPosition(mapEpisode.m_EpisodeCenter);
        ClearMapFocusCache();
    }

    ///////////////
    public MapEpisode GetEpisode(string id)
    {
        if (string.IsNullOrEmpty(id))
            return null;

        return m_Episodes.TryGetValue(id, out var result) ? result : null;
    }

    ///////////////
    public bool IsLoaded()
    {
        // когда карта проинициализирована, на ней должны быть эпизоды
        return m_Episodes.Count > 0;
    }
}