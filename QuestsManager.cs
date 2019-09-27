using System;
using System.Collections.Generic;
using SimpleJson;
using UnityEngine;
using System.Collections;

public class QuestsManager : Singleton<QuestsManager>, IGameManager
{
    // QuestsManager
    public readonly List<DailyQuestInfo> m_DailyQuests = new List<DailyQuestInfo>();
    public readonly List<MainQuestInfo> m_MainQuests = new List<MainQuestInfo>();

    public LoginQuestInfo LoginQuest { get; private set; }
    public DailyEnergyInfo DailyEnergy { get; private set; }

    public const int DailyEnergyFirstStep = 1;  // константа

    private bool m_IsDailyRewardWaiting;
    private bool m_IsLoginRewardWaiting;
    private bool m_IsMainRewardWaiting;
    private bool m_IsDailyEnergyWaiting;
    private bool m_IsInited;
    private DateTime m_DailyQuestsResetTime = DateTime.MinValue;
    private Coroutine m_EnergyUpdateCoroutine;

    public static event Action OnQuestRewardNotification;
    public static event Action OnDailyEnergyNotification;
    public static event Action OnDailyEnergyCollected; 

    ///////////////
    protected QuestsManager()
    {
        GenericGameManager.Instance.AddManager(this);
    }

    ///////////////
    public void Reset()
    {
        m_IsInited = false;
        m_DailyQuests.Clear();
        m_MainQuests.Clear();
        LoginQuest = null;
        DailyEnergy = null;

        m_DailyQuestsResetTime = DateTime.MinValue;

        m_IsDailyRewardWaiting = false;
        m_IsMainRewardWaiting = false;
        m_IsDailyEnergyWaiting = false;

        if (m_EnergyUpdateCoroutine != null)
        {
            StopCoroutine(m_EnergyUpdateCoroutine);
            m_EnergyUpdateCoroutine = null;
        }

        if (OnQuestRewardNotification != null)
            OnQuestRewardNotification();
    }

    ///////////////
    public void Init(JsonObject json)
    {
        if (m_IsInited)
            return;

        m_DailyQuestsResetTime = Helper.ParseGameTimer(GameData.Instance.Global.QuestsDailyResetTimerId);
        
        // парсим main quests
        JsonArray mainQuests = json.Get<JsonArray>("main");

        for (int i = 0; i < mainQuests.Count; i++)
        {
            JsonObject questInfo = mainQuests.GetAt<JsonObject>(i);

            if (questInfo.GetBool("rewarded") != true)
            {
                var info = new MainQuestInfo(questInfo);
                m_MainQuests.Add(info);
            }
        }

        SetNotificationMain();

        // парсим DailyQuests
        JsonArray dailyQuests = json.Get<JsonArray>("daily");

        for (int i = 0; i < dailyQuests.Count; i++)
        {
            JsonObject questInfo = dailyQuests.GetAt<JsonObject>(i);
            var info = new DailyQuestInfo(questInfo);

            if (info.m_Data != null)
                m_DailyQuests.Add(info);
        }

        // парсим LoginQuests
        JsonObject loginQuests = json.Get<JsonObject>("login");
        LoginQuest = new LoginQuestInfo();
        LoginQuest.UpdateLoginQuestInfo(loginQuests);
        m_IsLoginRewardWaiting = IsLoginRewardWaiting();
        SetNotificationLogin();

        // заводим daily energy
        JsonObject energy = json.Get<JsonObject>("daily_energy");
        DailyEnergy = new DailyEnergyInfo(energy);

        // проверяем, когда был последний апдейт
        DateTime lastUpdate = Helper.ParseGameTimer(GameData.Instance.Global.QuestsDailyResetTimerId, DailyEnergy.LastRewardTime);
        TimeSpan diff = m_DailyQuestsResetTime - lastUpdate;
            
        if (diff > TimeSpan.Zero)
        {
            DailyEnergy.ResetStep();
        }

        // обновляем нотифы завершенности квестов
        SetNotificationDaily();
        SetNotificationEnergy();
    }

    ///////////////
    public void UpdateDailyQuests(QuestGoalDaily goal, int amount)
    {
        TryResetDailyQuests();

        foreach (var quest in m_DailyQuests)
        {
            quest.UpdateGoal(goal, amount);
        }

        SetNotificationDaily();
    }

    ///////////////
    public void UpdateMainQuests(QuestGoalMain goal, int amount)
    {
        foreach (var quest in m_MainQuests)
        {
            quest.UpdateGoal(goal, amount);
        }

        SetNotificationMain();
    }

    ///////////////
    public void DailyQuestGetReward(DailyQuestInfo quest, Action callback)
    {
        DailyQuestData questData = quest.m_Data;

        if (questData.Gold > 0)
        {
            UIManager.Instance.AddGoldAnimated(questData.Gold);
        }

        if (questData.Crystals > 0)
        {
            UIManager.Instance.AddGemsAnimated(questData.Crystals);
        }

        EnergySnapshot snapshot = null;

        var matData = MaterialsDataStorage.Instance.GetById(questData.MaterialId);

        if (matData != null)
        {
            if (matData.MaterialType == MaterialType.EnergyConsumeTicket)
            {
                // автоматическое начисление энергии
                snapshot = UIManager.Instance.AddEnergyAnimated(GameData.Instance.Global.EnergyConsumeAmount * questData.MaterialAmount);
            }
            else
            {
                PlayerProfile.Instance.Inventory.AddMaterial(questData.MaterialId, questData.MaterialAmount);
            }
        }

        // при начислении опыта проверяем, не поднялся ли уровень
        if (questData.Xp > 0)
        {
            int prevPlayerLevel = PlayerProfile.Instance.Level;
            PlayerProfile.Instance.AddExp(questData.Xp, "daily quest");
            int currPlayerLevel = PlayerProfile.Instance.Level;
            int levelDiff = currPlayerLevel - prevPlayerLevel;

            if (levelDiff > 0)
            {
                // показать левел ап
                UIManager.Instance.GetDialog<LevelUpDialog>().Show();
                snapshot = PlayerProfile.Instance.GetEnergySnapShot();
            }
        }
        
        // закрыть пройденный квест
        quest.FinishQuest();
        SetNotificationDaily();
        
        // получить и начислить награду
        NetworkManager.Instance.DailyQuestGetReward(questData.Id, response =>
        {
            // синхронизация энергии
            if (snapshot != null)
            {
                PlayerProfile.Instance.SyncEnergy(response.Get<JsonObject>("energy"), snapshot);
            }

            // отправляем ивент траты алмазов
            if (questData.Crystals > 0)
            {
                Analytics.LogCrystalsAmountChangeEvent(true, "daily quest", questData.Crystals, questData.Id);
            }

            // проверка на получение нового героя
            if (response.ContainsKey("soul") && response["soul"] != null)
            {
                ParseNewHero(response);
            }

            if (callback != null)
                callback();
        });
    }

    ///////////////
    public void MainQuestGetReward(MainQuestInfo quest, Action callback)
    {
        MainQuestData questData = quest.m_Data;

        if (questData.Gold > 0)
        {
            UIManager.Instance.AddGoldAnimated(questData.Gold);
        }

        if (questData.Crystals > 0)
        {
            UIManager.Instance.AddGemsAnimated(questData.Crystals);
        }

        EnergySnapshot snapshot = null;

        var matData = MaterialsDataStorage.Instance.GetById(questData.MaterialId);

        if (matData != null)
        {
            if (matData.MaterialType == MaterialType.EnergyConsumeTicket)
            {
                // автоматическое начисление энергии
                snapshot = UIManager.Instance.AddEnergyAnimated(GameData.Instance.Global.EnergyConsumeAmount * questData.MaterialAmount);
            }
            else
            {
                PlayerProfile.Instance.Inventory.AddMaterial(questData.MaterialId, questData.MaterialAmount);
            }
        }

        // при начислении опыта проверяем, не поднялся ли уровень
        if (questData.Xp > 0)
        {
            int prevPlayerLevel = PlayerProfile.Instance.Level;
            PlayerProfile.Instance.AddExp(questData.Xp, "main quest");
            int currPlayerLevel = PlayerProfile.Instance.Level;
            int levelDiff = currPlayerLevel - prevPlayerLevel;

            if (levelDiff > 0)
            {   
                snapshot = PlayerProfile.Instance.GetEnergySnapShot();
                // показать левел ап
                UIManager.Instance.GetDialog<LevelUpDialog>().Show();
            }
        }

        // получить и начислить награду
        NetworkManager.Instance.MainQuestGetReward(questData.Id, response =>
        {
            // синхронизация энергии
            if (snapshot != null)
            {
                PlayerProfile.Instance.SyncEnergy(response.Get<JsonObject>("energy"), snapshot);
            }

            // проверка на получение нового героя
            if (response.ContainsKey("soul") && response["soul"] != null)
            {
                ParseNewHero(response);
            }

            if (questData.Crystals > 0)
            {
                // заливаем аналитику изменения баланса алмазов
                Analytics.LogCrystalsAmountChangeEvent(true, "main quest", questData.Crystals, questData.Id);
            }

            if (callback != null)
                callback();
        });

        // закрыть пройденный квест
        quest.FinishQuest();
        m_MainQuests.Remove(quest);
        SetNotificationMain();
    }

    ///////////////
    public void LoginQuestGetReward(Action callback = null)
    {
        LoginQuestData questData = LoginQuest.GetCurrentData();

        // TODO: для ВИПа награда х2

        if (questData.Gold > 0)
        {
            UIManager.Instance.AddGoldAnimated(questData.Gold);
        }

        if (questData.Crystals > 0)
        {
            UIManager.Instance.AddGemsAnimated(questData.Crystals);
        }

        EnergySnapshot snapshot = null;

        var matData = MaterialsDataStorage.Instance.GetById(questData.MaterialId);

        if (matData != null)
        {
            if (matData.MaterialType == MaterialType.EnergyConsumeTicket)
            {
                // автоматическое начисление энергии
                snapshot = UIManager.Instance.AddEnergyAnimated(GameData.Instance.Global.EnergyConsumeAmount * questData.MaterialAmount);
            }
            else
            {
                PlayerProfile.Instance.Inventory.AddMaterial(questData.MaterialId, questData.MaterialAmount);
            }
        }

        // получить и начислить награду
        NetworkManager.Instance.LoginQuestGetReward(response =>
        {
            LoginQuest.UpdateLoginQuestInfo(response);

            // синхронизация энергии
            if (snapshot != null)
            {
                PlayerProfile.Instance.SyncEnergy(response.Get<JsonObject>("energy"), snapshot);
            }

            // проверка на получение нового героя
            if (response.ContainsKey("soul") && response["soul"] != null)
            {
                ParseNewHero(response);
            }

            if (response.ContainsKey("equipment") && response["equipment"] != null)
            {
                var item = new EquipmentPlayerItem(response.Get<JsonObject>("equipment"));
                PlayerProfile.Instance.Inventory.AddToInventory(item, true);
            }

            if (questData.Crystals > 0)
            {
                // заливаем аналитику изменения баланса алмазов
                Analytics.LogCrystalsAmountChangeEvent(true, "login quest", questData.Crystals, questData.Id);
            }

            if (callback != null)
                callback();

            SetNotificationLogin();
        });
    }

    /// <summary>
    /// Добавляем нового героя, аналогично покупке в маркете
    /// </summary>
    private void ParseNewHero(JsonObject json)
    {
        JsonObject soul = json.Get<JsonObject>("soul");
        UnitInfo newHero = Helper.ParseNewHero(soul);

        // показываем диалог получения героя
        if (newHero != null)
        {
            UIManager.Instance.GetDialog<NewHeroDialog>().Show(newHero.GetData());
        }
    }

    /// <summary>
    /// Добавляет новые квесты, которые стали доступны по уровню
    /// Вызывается при повышении уровня.
    /// </summary>
    public void AddNewQuestsOnLevelUp()
    {
        List<DailyQuestData> dailyQuestData = DailyQuestsDataStorage.Instance.GetData();
        DailyQuestData[] questsQuery = new DailyQuestData[(int)QuestGoalDaily.Count];

        // формируем массив доступных квестов
        foreach (DailyQuestData questData in dailyQuestData)
        {
            int index = (int)questData.Goal;

            if (questsQuery[index] == null || questsQuery[index].RequiredPlayerLevel < questData.RequiredPlayerLevel)
            {
                // При выдаче квестов, выбираем квест с максимальным доступным min_level для каждого Goal
                if (questData.RequiredPlayerLevel <= PlayerProfile.Instance.Level)
                {
                    questsQuery[index] = questData;
                }
            }
        }

        foreach (DailyQuestData questData in questsQuery)
        {
            if (questData != null)
            {
                DailyQuestInfo existingQuest = m_DailyQuests.Find(x => x.m_Data.Goal == questData.Goal);

                // Не выдаем новый квест если уже есть квест с таким Goal.
                if (existingQuest != null)
                    continue;

                m_DailyQuests.Add(new DailyQuestInfo(questData));
            }
        }
    }

    /// <summary>
    /// Добавляет все квесты, которые доступны по уровню
    /// Вызывается при ресете.
    /// </summary>
    private void InitDailyQuests()
    {
        List<DailyQuestData> dailyQuestData = DailyQuestsDataStorage.Instance.GetData();
        DailyQuestData[] questsQuery = new DailyQuestData[(int)QuestGoalDaily.Count];

        // формируем массив доступных квестов
        foreach (DailyQuestData questData in dailyQuestData)
        {
            int index = (int)questData.Goal;

            if (questsQuery[index] == null || questsQuery[index].RequiredPlayerLevel < questData.RequiredPlayerLevel)
            {
                // При выдаче квестов, выбираем квест с максимальным доступным min_level для каждого Goal
                if (questData.RequiredPlayerLevel <= PlayerProfile.Instance.Level)
                {
                    questsQuery[index] = questData;
                }
            }
        }

        m_DailyQuests.Clear();

        foreach (DailyQuestData questData in questsQuery)
        {
            if (questData != null)
            {
                m_DailyQuests.Add(new DailyQuestInfo(questData));
            }
        }
    }

    /// <summary>
    /// Время до ресета дейликов
    /// </summary>
    public TimeSpan GetTimeToRefresh()
    {
        return m_DailyQuestsResetTime - ServerTime.UtcNow();
    }

    /// <summary>
    /// Проверяем по таймеру, нужно ли обновить квесты
    /// </summary>
    public void TryResetDailyQuests()
    {
        TimeSpan ts = GetTimeToRefresh();

        if (ts <= TimeSpan.Zero)
        {
            // таймер обновился; делаем ресет 
            m_DailyQuestsResetTime = Helper.ParseGameTimer(GameData.Instance.Global.QuestsDailyResetTimerId);
            // все награды пропадают, если они есть
            InitDailyQuests();

            SetNotificationDaily();
            SetNotificationLogin();

            // ресетим энергию
            if (DailyEnergy != null)
            {
                DailyEnergy.ResetStep();
                SetNotificationEnergy();
            }
        }
    }

    /// <summary>
    /// Получить бонусную энергию
    /// </summary>
    public void DailyEnergyGetReward()
    {
        DailyEnergyData energyData = DailyEnergy.CurrentStageData;

        // получить и начислить энергию
        EnergySnapshot snapshot = UIManager.Instance.AddEnergyAnimated(energyData.Energy);
        // ... а также кристаллы
        UIManager.Instance.AddGemsAnimated(energyData.Crystals);

        // переходим на следующий этап выдачи энергии
        DailyEnergy.Advance(ServerTime.UtcNow());

        NetworkManager.Instance.DailyEnergyGetReward(response =>
        {
            if (snapshot != null)
            {
                // синхронизация энергии
                JsonObject energy = response.Get<JsonObject>("energy");
                PlayerProfile.Instance.SyncEnergy(energy, snapshot);
            }

            if (energyData.Crystals > 0)
            {
                Analytics.LogCrystalsAmountChangeEvent(true, "daily energy", energyData.Crystals, energyData.Id);
            }
        });

        if (OnDailyEnergyCollected != null)
            OnDailyEnergyCollected();

        SetNotificationEnergy();
    }

    /// <summary>
    /// Устанавливаем/снимаем нотифы (красные точки) для daily quests 
    /// </summary>
    private void SetNotificationDaily()
    {
        bool reward = false;

        foreach (var quest in m_DailyQuests)
        {
            // проверяем, есть ли выполненные квесты, за которые не взята награда
            if (!quest.IsRewarded && quest.IsCompleted())
            {
                reward = true;
                break;
            }
        }

        if (reward != m_IsDailyRewardWaiting)
        {
            // если флаг изменился, рассылаем нотификацию
            m_IsDailyRewardWaiting = reward;

            if (OnQuestRewardNotification != null)
                OnQuestRewardNotification();
        }
    }

    /// <summary>
    /// Устанавливаем/снимаем нотифы (красные точки) для main quests 
    /// </summary>
    private void SetNotificationMain()
    {
        bool reward = false;

        foreach (var quest in m_MainQuests)
        {
            // проверяем, есть ли выполненные квесты, за которые не взята награда
            if (quest.IsCompleted() && !quest.IsRewarded)
            {
                reward = true;
                break;
            }
        }

        if (reward != m_IsMainRewardWaiting)
        {
            // если флаг изменился, рассылаем нотификацию
            m_IsMainRewardWaiting = reward;

            if (OnQuestRewardNotification != null)
                OnQuestRewardNotification();
        }
    }

    /// <summary>
    /// Устанавливаем/снимаем нотифы (красные точки) для login quests 
    /// </summary>
    private void SetNotificationLogin()
    {
        bool reward = IsLoginRewardWaiting();

        if (reward != m_IsLoginRewardWaiting)
        {
            // если флаг изменился, рассылаем нотификацию
            m_IsLoginRewardWaiting = reward;

            if (OnQuestRewardNotification != null)
                OnQuestRewardNotification();
        }
    }

    /// <summary>
    /// Устанавливаем/снимаем нотифы (красные точки) для daily energy 
    /// </summary>
    private void SetNotificationEnergy()
    {
        int curStep = DailyEnergy.CurrentStep;
        var energyData = DailyEnergy.CurrentStageData;
        DateTime curStepTime;

        if (energyData == null)
        {
            // следующего шага нет, ждем до ресета дейликов
            curStepTime = m_DailyQuestsResetTime;
        }
        else
        {
            // вычисляем следующий шаг, отталкиваясь от времени взятия предыдущего, и задержки между ними
            curStepTime = (curStep > DailyEnergyFirstStep) ? DailyEnergy.LastRewardTime : ServerTime.UtcNow();
            curStepTime = curStepTime.AddSeconds(energyData.Delay);

            if (curStepTime > m_DailyQuestsResetTime)
            {
                // если ресет будет раньше, чем следующий шаг, ждем ресета
                curStepTime = m_DailyQuestsResetTime;
            }
        }

        DateTime now = ServerTime.UtcNow();
        TimeSpan dt = curStepTime - now;
        // если время вышло, значит, нас ждет награда
        bool reward = dt <= TimeSpan.Zero;

        if (!reward)
        {
            if (m_EnergyUpdateCoroutine != null)
            {
                StopCoroutine(m_EnergyUpdateCoroutine);
                m_EnergyUpdateCoroutine = null;
            }
            // запускаем ожидание времени следующего шага сбора энергии
            m_EnergyUpdateCoroutine = StartCoroutine(EnergyUpdateTimer(dt));
        }

        if (reward != m_IsDailyEnergyWaiting)
        {
            // если флаг изменился, рассылаем нотификацию
            m_IsDailyEnergyWaiting = reward;

            OnDailyEnergyNotification?.Invoke();
        }
    }

    /// <summary>
    /// Ведем отсчет таймера до следующего шага сбора энергии
    /// </summary>
    private IEnumerator EnergyUpdateTimer(TimeSpan timeSpan)
    {
        float totalSeconds = (float)timeSpan.TotalSeconds;

        yield return new WaitForSecondsRealtime(totalSeconds + 1f);  // 1 сек оставляем на "подумать"

        m_EnergyUpdateCoroutine = null;
        // проверяем состояние энергии
        SetNotificationEnergy();
    }

    /// <summary>
    /// Проверяем наличие награды в квестах
    /// </summary>
    public bool IsRewardWaiting()
    {
        if (!IsQuestsAvailable())
            return false;

        if (m_IsMainRewardWaiting || m_IsDailyRewardWaiting || IsLoginRewardWaiting())
            return true;

        return false;
    }

    /// <summary>
    /// Проверяем наличие награды в квестах
    /// </summary>
    public bool IsDailyRewardWaiting()
    {
        return IsQuestsAvailable() && m_IsDailyRewardWaiting;
    }

    /// <summary>
    /// Проверяем наличие награды в квестах
    /// </summary>
    public bool IsDailyEnergyWaiting()
    {
        return IsDailyEnergyAvailable() && m_IsDailyEnergyWaiting;
    }

    /// <summary>
    /// Проверяем наличие награды в квестах
    /// </summary>
    public bool IsMainRewardWaiting()
    {
        return IsQuestsAvailable() && m_IsMainRewardWaiting;
    }

    /// <summary>
    /// Проверка, что квест 'выполнить все ежедневные' можно считать выполненным
    /// </summary>
    public bool IsCompleteAllDailyQuestsGoalCompleted()
    {
        foreach (DailyQuestInfo dailyQuestInfo in m_DailyQuests)
        {
            if (dailyQuestInfo.m_Data.Goal == QuestGoalDaily.CompleteAllQuests)
                continue;
     
            // пока не собрали награду со всех ежедневных квестов не считаем
            // квест 'QuestGoalDaily.CompleteAllQuests' выполненным
            if (!dailyQuestInfo.IsRewarded)
                return false;
        }
        
        return true;
    }

    ///////////////
    public bool IsQuestsAvailable()
    {
        return PlayerProfile.Instance.IsMissionCompleted(GameData.Instance.PlayerData.QuestsRequiredMission);
    }

    ///////////////
    public bool IsDailyEnergyAvailable()
    {
        return PlayerProfile.Instance.IsMissionCompleted(GameData.Instance.PlayerData.DailyEnergyRequiredMission);
    }

    ///////////////
    public bool IsLoginRewardWaiting()
    {
        return HasLoginRewards() && LoginQuest.IsAvailable();
    }

    ///////////////
    public bool HasLoginRewards()
    {
        return LoginQuestsDataStorage.Instance.GetCycleData(LoginQuest.GetCurrentCycle()) != null;
    }
}