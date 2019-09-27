using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using UnityEngine.UI;
using TMPro;
using SimpleJson;
using DG.Tweening;
using Internal;

public enum RewardSource
{
    Campaign = 0,
    Grind = 1,
    Map = 2,
    Purchase = 3,
    SoulStone = 4
}

/// <summary>
/// Диалог показа награды за миссию
/// </summary>
public class RewardDialog : BaseDialog
{
    public GameObject m_Frame;
    public TMP_Text m_TapAnywhereHint;
    public TMP_Text m_NewRewardText;
    public GameObject m_RewardPanel;
    public RewardDialogItem m_RewardItemPrefab;

    private const float m_StartDelay = .2f;
    private const float m_TextFallTime = .25f;
    private const float m_RewardsInterval = .2f;
    private const string m_DotweenCollect = "reward_collect";
    private const float m_SpacingScale = 3.625f;
    private const float m_SpacingScaleIpX = 5.8f;
    private const int m_MaxLineCount = 5;
    private const int m_MaxLines = 3;
    private const int m_SingleLineCount = 3;

    private MissionData m_RewardedMission;

    private List<LootboxContentItemInfo> m_RewardsList = new List<LootboxContentItemInfo>();
    HashSet<string> m_NewHeroes = new HashSet<string>();


    ///////////////
    public void Show(JsonObject rewardInfo, RewardSource source, MissionData completedMission = null, MissionDifficulty missionDifficulty = MissionDifficulty.Normal)
    {
        Show();

        if (source == RewardSource.Purchase)
        {
            rewardInfo.Add("reward", rewardInfo);
            m_NewRewardText.text = Localization.Get("you_received");
        }
        else if (source == RewardSource.SoulStone)
        {
            m_NewRewardText.text = Localization.Get("you_received");
        }
        else
        {
            m_NewRewardText.text = Localization.Get("rewards_text");
        }

        int crystals;
        
        ParseResponse(rewardInfo, out crystals, completedMission, missionDifficulty);

        if (crystals > 0)
        {
            switch (source)
            {
                case RewardSource.Campaign:
                    Analytics.LogCrystalsAmountChangeEvent(true, "campaign mission", crystals);
                    break;

                case RewardSource.Grind:
                    Analytics.LogCrystalsAmountChangeEvent(true, "grind mission", crystals);
                    break;

                case RewardSource.Map:
                    Analytics.LogCrystalsAmountChangeEvent(true, "hard stars reward", crystals);
                    break;
            }
        }

        ShowReward();
    }

    ///////////////
    public void ShowMapChestReward(JsonObject json)
    {
        m_RewardsList.Clear();
        Show();

        int gold = json.GetInt("gold");
        int crystals = json.GetInt("crystals");

        if (gold > 0)
        {
            m_RewardsList.Add(new LootboxContentItemInfo(LootboxContentItemInfo.Type.Gold, gold, null, false));
            UIManager.Instance.AddGoldAnimated(gold);
        }

        if (crystals > 0)
        {
            m_RewardsList.Add(new LootboxContentItemInfo(LootboxContentItemInfo.Type.Crystals, crystals, null, false));
            UIManager.Instance.AddGemsAnimated(crystals);
        }

        ShowReward();
    }

    ///////////////
    public void GetMapReward(string rewardId)
    {
        Show();
        m_NewRewardText.gameObject.SetActive(false);
        m_TapAnywhereHint.gameObject.SetActive(false);

        ShowLoadingIndicator();
        LockDialog();

        NetworkManager.Instance.GetMapReward(rewardId, response =>
        {
            HideLoadingIndicator();
            UnlockDialog();

            if (response != null && response.ContainsKey("reward"))
            {
                Show(response, RewardSource.Map);
            }
        });
    }

    /// <summary>
    /// Включить/отключить кнопки в диалоге
    /// </summary>
    private void ActivateBackFrame(bool flag)
    {
        m_Frame.SetActive(flag);
        m_TapAnywhereHint.gameObject.SetActive(!flag);
    }

    /// <summary>
    /// Действия при закрытии окна
    /// </summary>
    public void OnCloseButtonClick()
    {
        if (DOTween.IsTweening(m_DotweenCollect))
        {
            DOTween.Complete(m_DotweenCollect, true);
            return;
        }

        Hide();
    }

    ///////////////
    protected override void OnHide()
    {
        base.OnHide();
        m_RewardsList.Clear();
        m_NewHeroes.Clear();
        m_RewardPanel.transform.DestroyChildren();
    }

    ///////////////
    private void ParseResponse(JsonObject content, out int crystalsAmount, MissionData completedMission, MissionDifficulty missionDifficulty)
    {
        crystalsAmount = 0;

        int gold = 0;

        JsonObject rewardInfo = (JsonObject)content["reward"];

        // если поле reward не null
        if (rewardInfo != null)
        {
            Helper.ParseLootboxContent(rewardInfo, m_NewHeroes, out gold, out int crystals, m_RewardsList, m_RewardsList);
            
            if (crystals > 0)
            {
                var info = new LootboxContentItemInfo(LootboxContentItemInfo.Type.Crystals, crystals, null, false);
                UIManager.Instance.AddGemsAnimated(crystals);
                m_RewardsList.Add(info);

                crystalsAmount = crystals;
            }
        }

        // учитываем в наградах золото за миссию
        if (completedMission != null)
        {
            DateTime processTime = Helper.ParseDateTime((string)content["timestamp"]);
            gold += completedMission.GetGoldReward(missionDifficulty, processTime);
        }

        if (gold > 0)
        {
            var info = new LootboxContentItemInfo(LootboxContentItemInfo.Type.Gold, gold, null, false);
            UIManager.Instance.AddGoldAnimated(gold);
            m_RewardsList.Add(info);
        }

        // применяем сортировку для лута
        Helper.LootboxItemsSort(m_RewardsList, true);
    }

    ///////////////
    public void ShowSingleMaterialReward(string materialId, int materialAmount)
    {
        Show();

        if (materialId != MaterialsDataStorage.Instance.EnergyConsumeTicket.Id)
        {
            m_RewardsList.Add(new LootboxContentItemInfo(LootboxContentItemInfo.Type.Material, materialAmount, materialId, false));
        }
        else
        {
            // когда покупаем только энергию, надо разделить их количество по одному на ячейку
            for (int i = 0; i < materialAmount; i++)
            {
                m_RewardsList.Add(new LootboxContentItemInfo(LootboxContentItemInfo.Type.Material, 1, materialId, false));
            }
        }

        ShowReward();
    }

    ///////////////
    public void ShowSingleCurrencyReward(bool isGold, int amount)
    {
        Show();

        LootboxContentItemInfo.Type type = isGold ? LootboxContentItemInfo.Type.Gold : LootboxContentItemInfo.Type.Crystals;

        m_RewardsList.Add(new LootboxContentItemInfo(type, amount, null, false));

        ShowReward();
    }

    ///////////////
    private void ShowReward()
    {
        // нужно, чтобы срабатывал клик по области, прерывающий анимацию
        ActivateBackFrame(true);

        Sequence sequence = DOTween.Sequence().SetId(m_DotweenCollect);
        sequence.AppendInterval(m_StartDelay);

        // надпись Награды "выпадает" сверху и появляется из прозрачности

        Rect dialogRect = gameObject.GetComponent<RectTransform>().rect;
        Rect rect = m_NewRewardText.gameObject.GetComponent<RectTransform>().rect;
        // вычисляем необходимое смещение за пределы экрана
        float offsetY = dialogRect.height / 2 + rect.height / 2;
        float onScreen = m_NewRewardText.transform.localPosition.y;

        // ставим надпись за пределы экрана
        Vector3 pos = m_NewRewardText.transform.localPosition;
        pos.y = offsetY;
        m_NewRewardText.transform.localPosition = pos;
        m_NewRewardText.gameObject.SetActive(true);

        sequence.Append(DOTween.Sequence().Append(m_NewRewardText.transform.DOLocalMoveY(onScreen - rect.height, m_TextFallTime))
                                        .Append(m_NewRewardText.transform.DOLocalMoveY(onScreen, m_TextFallTime)).SetEase(Ease.OutQuad))
                .Join(DOTween.Sequence().Append(m_NewRewardText.DOFade(0f, 0.01f))
                                        .Append(m_NewRewardText.DOFade(1f, m_TextFallTime * 2).SetEase(Ease.Linear)));

        // задаем константы для смещения ячеек
        Vector2 cellSize = m_RewardItemPrefab.gameObject.GetComponent<RectTransform>().sizeDelta;
        // Подстраивает скейл между ячейками UI на разных соотношениях сторон (в основном для iphone-x aspect = 2.16)
        // см. CustomCanvasScaler
        float aspectRatio = Screen.width / (float)Screen.height;
        float scale = aspectRatio >= 2f ? m_SpacingScaleIpX : m_SpacingScale;
        float spacingX = cellSize.x / scale;
        float spacingY = cellSize.y / scale;
        float deltaX = cellSize.x + spacingX;
        float deltaY = cellSize.y + spacingY;

        // создаем сетку выпавшего лута
        int totalCells = m_RewardsList.Count;
        int divider = totalCells <= m_MaxLineCount ? m_SingleLineCount : m_MaxLineCount;
        int totalLines = Mathf.CeilToInt((float)totalCells / divider);
        int topLineCount = totalLines < m_MaxLines ? Mathf.CeilToInt((float)totalCells / totalLines) : m_MaxLineCount;

        for (int curIndex = 0; curIndex < totalCells; curIndex++)
        {
            LootboxContentItemInfo info = m_RewardsList[curIndex];
            var prefab = Instantiate(m_RewardItemPrefab, m_RewardPanel.transform);
            prefab.SetInfo(info);
            prefab.gameObject.SetActive(false);

            // выставляем позицию текущей ячейки, считая от центра
            // награды можно располагать в одну, две или три строки
            // первые две строки заполняются синхронно, третья - по заполнению 1 и 2
            int curLine = Mathf.CeilToInt((float)(curIndex + 1) / topLineCount);
            bool topLine = curIndex < topLineCount;
            int rest = topLineCount * (curLine - 1);
            int curLineCount = topLine ? topLineCount : Mathf.Min(totalCells - rest, m_MaxLineCount);
            float halfCount = (float)(curLineCount - 1) / 2;
            float deltaCount = topLine ? (curIndex - halfCount) : (curIndex - rest - halfCount);

            float posX = deltaX * deltaCount;
            float posY = ((float)(totalLines + 1) / 2 - curLine) * deltaY;

            prefab.transform.localPosition = new Vector3(posX, posY, 0f);

            // ставим в очередь анимацию появления наград
            sequence.Insert(m_StartDelay + m_TextFallTime + curIndex * m_RewardsInterval, prefab.Animate());
        }

        sequence.OnComplete(() =>
        {
            ActivateBackFrame(false);

            // по завершении анимации показываем новых героев 
            foreach (string heroId in m_NewHeroes)
            {
                UnitData hero = UnitsDataStorage.Instance.GetById(heroId);

                if (hero != null)
                    UIManager.Instance.GetDialog<NewHeroDialog>().Show(hero);
            }
        });
    }
}
