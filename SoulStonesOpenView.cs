using System.Collections;
using SimpleJson;
using UnityEngine;
using EnhancedUI.EnhancedScroller;
using TMPro;
using Spine.Unity;
using Spine;
using UnityEngine.UI;
using System.Collections.Generic;

public class SoulStonesOpenView : MageTowerTabView, IEnhancedScrollerDelegate
{
    public SoulStoneCellView m_TopCell;
    public EnhancedScroller m_Scroller;
    public SoulStoneCellView m_ScrollPrefab;
    public TextMeshProUGUI m_SingleSummonPriceText;
    public TextMeshProUGUI m_MultipleSummonPriceText;
    public TextMeshProUGUI m_MultipleSummonButtonText;
    public TextMeshProUGUI m_SingleSummonButtonText;
    public Button m_SingleSummonButton;
    public Button m_MultipleSummonButton;

    public SkeletonGraphic m_StoneAnimation;
    public GameObject m_BackBreakFx;
    public GameObject m_BackIdleFx;
    public GameObject m_StoneBreakFx;
    public GameObject m_StoneIdleFx;

    private SoulStoneData m_CurrentSoulStone;
    private JsonObject m_RequestResponse = null;
    private List<MaterialData> m_SoulStones;
    private Coroutine m_WaitRewardsCoroutine;

    private const string AnimationIdleName = "Idle";
    private const string AnimationBreakName = "Break";

    private enum SummonType
    {
        Single = 0,
        Multiple = 1
    }

    /////////////////
    private void OnEnable()
    {
        SoulStoneCellView.OnClickItem += SetMaterial;
        BaseDialog.OnDialogHide += ShowSoulStone;
    }

    /////////////////
    private void OnDisable()
    {
        SoulStoneCellView.OnClickItem -= SetMaterial;
        BaseDialog.OnDialogHide -= ShowSoulStone;
    }
    
    /////////////////
    private void Start()
    {
        m_Scroller.Delegate = this;
        m_Scroller.cellViewVisibilityChanged = OnCellVisibilityChanged;
    }

    /////////////////
    private void SetMaterial(string material)
    {
        m_CurrentSoulStone = SoulStonesDataStorage.Instance.GetSoulStoneDataByMaterialId(material);

        // обновляем анимацию
        m_StoneAnimation.Skeleton.SetSkin(m_CurrentSoulStone.StoneType.ToString());
        m_StoneAnimation.Skeleton.SetSlotsToSetupPose();
        m_StoneAnimation.AnimationState.Apply(m_StoneAnimation.Skeleton);

        // обновляем ценники и текст  на кнопке множественного призыва
        UpdatePriceTexts();
    }

    /////////////////
    private void UpdatePriceTexts()
    {
        int cost = m_CurrentSoulStone.GoldCostPerStone; // проверяем призыв 1 камня

        // состояние кнопки зависит от наличия материалов
        if (PlayerProfile.Instance.Inventory.GetMaterialsAmount(m_CurrentSoulStone.MaterialId) > 0)
        {
            m_SingleSummonButton.image.color = Color.white;
            m_SingleSummonButtonText.color = Color.white;
        }
        else
        {
            m_SingleSummonButton.image.color = Color.gray;
            m_SingleSummonButtonText.color = Color.gray;
        }

        // цвет текста с ценой зависит от наличия золота
        m_SingleSummonPriceText.text = PlayerProfile.Instance.EnoughGold(cost) ? cost.ToString() : Helper.SetTextColor(cost.ToString(), TextColors.Red);

        cost = cost * Constants.SoulStoneMultipleSummonAmount; // проверяем множественный призыв

        // состояние кнопки зависит от наличия материалов
        if (PlayerProfile.Instance.Inventory.GetMaterialsAmount(m_CurrentSoulStone.MaterialId) >= Constants.SoulStoneMultipleSummonAmount)
        {
            m_MultipleSummonButton.image.color = Color.white;
            m_MultipleSummonButtonText.color = Color.white;
        }
        else
        {
            m_MultipleSummonButton.image.color = Color.gray;
            m_MultipleSummonButtonText.color = Color.gray;
        }

        // цвет текста с ценой зависит от наличия золота
        m_MultipleSummonPriceText.text = PlayerProfile.Instance.EnoughGold(cost) ? cost.ToString() : Helper.SetTextColor(cost.ToString(), TextColors.Red);

        m_MultipleSummonButtonText.text = Localization.Get("btn_summon") + " x" + Constants.SoulStoneMultipleSummonAmount.ToString();
    }

    /////////////////
    public override void UpdateView()
    {
        if (m_CurrentSoulStone == null)
        {
            SetMaterial(MaterialsDataStorage.Instance.m_SoulStones[0].Id);
        }
        else
        {
            SetMaterial(m_CurrentSoulStone.MaterialId);
        }

        m_SoulStones = new List<MaterialData>(MaterialsDataStorage.Instance.m_SoulStones);

        foreach (MaterialData stone in m_SoulStones)
        {
            SoulStoneData data = SoulStonesDataStorage.Instance.GetSoulStoneDataByMaterialId(stone.Id);

            if (data.StoneType == SoulStoneType.Chaos)
            {
                m_SoulStones.Remove(stone);
                m_TopCell.UpdateView(stone);
                m_TopCell.SetSelected(m_CurrentSoulStone.MaterialId);
                break;
            }
        }

        m_Scroller.ReloadData();

        SetupAnimation(AnimationIdleName);
    }

    /////////////////
    public void OnClickSummon(int summonTypeIndex)
    {
        SummonType summonType = (SummonType)summonTypeIndex;

        int materialsToSpend;

        if (summonType == SummonType.Single)
        {
            materialsToSpend = 1;
        }
        else
        {
            materialsToSpend = Constants.SoulStoneMultipleSummonAmount;
        }

        if (PlayerProfile.Instance.Inventory.GetMaterialsAmount(m_CurrentSoulStone.MaterialId) < materialsToSpend)
        {
            UIManager.Instance.GetDialog<MageTowerConfirmPopup>()?.Show();
            return;
        }

        int summonCost = m_CurrentSoulStone.GoldCostPerStone * materialsToSpend;

        if (!PlayerProfile.Instance.EnoughGold(summonCost, true))
            return;

        // после начала открытия запускаем анимацию и эффекты призыва и активируем корутину
        // если ответ не приходит до конца, то показываем вертушку
        SetupAnimation(AnimationBreakName);

        PlayerProfile.Instance.TrySpendGold(summonCost);
        PlayerProfile.Instance.Inventory.RemoveMaterial(m_CurrentSoulStone.MaterialId, materialsToSpend);
        UpdatePriceTexts();
        
        // отправляем запрос к серверу на получение душ
        NetworkManager.Instance.UseSoulStones(m_CurrentSoulStone.MaterialId, materialsToSpend, response =>
        {
            m_RequestResponse = response;
        });

        if (m_WaitRewardsCoroutine != null)
        {
            StopCoroutine(WaitForRewards());
            m_WaitRewardsCoroutine = null;
        }

        m_WaitRewardsCoroutine = StartCoroutine(WaitForRewards());
    }

    /////////////////
    private void SetupAnimation(string animationName)
    {
        if (animationName.Equals(AnimationBreakName))
        {
            m_StoneAnimation.AnimationState.SetAnimation(0, animationName, false);
            m_StoneBreakFx.SetActive(true);
            m_StoneIdleFx.SetActive(false);
            m_BackBreakFx.SetActive(true);
            m_BackIdleFx.SetActive(false);
        }
        else
        {
            m_StoneAnimation.AnimationState.SetAnimation(0, animationName, true);
            m_StoneBreakFx.SetActive(false);
            m_StoneIdleFx.SetActive(true);
            m_BackBreakFx.SetActive(false);
            m_BackIdleFx.SetActive(true);
        }

        m_StoneAnimation.Skeleton.SetToSetupPose();
        m_StoneAnimation.Update(0);
    }

    /////////////////
    private IEnumerator WaitForRewards()
    {
        var dialog = UIManager.Instance.GetDialog<MageTowerDialog>();
        // лочим диалог, чтобы нельзя было его закрыть до получения наград
        dialog.LockDialog();

        bool isClicked = false;
        bool isCompleted = false;

        while (true)
        {
            if (Input.GetMouseButtonDown(0))
            {
                // ловим скип по тапу, чтобы показать награды сразу, как придет ответ
                isClicked = true;
            }
        
            if (m_StoneAnimation.AnimationState.GetCurrent(0).IsComplete)
            {
                // если кончилась анимация, переходим к показу наград, если есть ответ
                isCompleted = true;
            }

            if (m_RequestResponse == null && isCompleted)
            {
                // анимация закончилась, а ответ на запрос еще не пришел, включаем вертушку
                dialog.ShowLoadingIndicator();
                // ждем ответа
                yield return new WaitWhile(() => m_RequestResponse == null);
            }

            if (m_RequestResponse != null && (isClicked || isCompleted))
            {
                dialog.UnlockDialog();
                dialog.HideLoadingIndicator();
                // показываем диалог получения награды и прерываем корутину
                UIManager.Instance.GetDialog<RewardDialog>().Show(m_RequestResponse, RewardSource.SoulStone);
                m_RequestResponse = null;
                m_WaitRewardsCoroutine = null;
                yield break;
            }

            yield return null;
        }
    }

    /////////////////
    private void ShowSoulStone(BaseDialog dialog)
    {
        // метод для обновления отображаения камня после сбора награды
        if (dialog is RewardDialog)
        {
            SetupAnimation(AnimationIdleName);
        }
    }

    /////////////////
    private void OnCellVisibilityChanged(EnhancedScrollerCellView cell)
    {
        SoulStoneCellView view = (SoulStoneCellView)cell;

        view.SetSelected(m_CurrentSoulStone.MaterialId);
    }

    #region Enhanced Scroller Handlers
    /////////////////
    public int GetNumberOfCells(EnhancedScroller scroller)
    {
        return m_SoulStones.Count;
    }

    /////////////////
    public float GetCellViewSize(EnhancedScroller scroller, int dataIndex)
    {
        return m_ScrollPrefab.GetComponent<RectTransform>().rect.height;
    }

    /////////////////
    public EnhancedScrollerCellView GetCellView(EnhancedScroller scroller, int dataIndex, int cellIndex)
    {
        SoulStoneCellView cell = scroller.GetCellView(m_ScrollPrefab) as SoulStoneCellView;

        cell.UpdateView(m_SoulStones[dataIndex]);

        return cell;
    }
    #endregion
}
