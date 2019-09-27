using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using SimpleJson;
using DG.Tweening;
using UnityEngine.Rendering;

/// <summary>
/// Класс диалога показа ("вылета") лутбокса из окна победы
/// </summary>
public class ShowLootboxDialog : BaseDialog
{
    public Transform m_AnchorStart;  // позиция лутбокса в окне победы, над кнопкой
    public Transform m_AnchorCenter;    // позиция лутбокса в этом диалоге, в центре экрана
    public GameObject m_Frame;
    public Button m_Open;
    public Image m_ButtonAdditive;
    public Image m_CurrencyGroup;
    public GameObject m_NoPrice;
    public TMP_Text m_Currency;
    public TMP_Text m_BtnOpenText;
    public TMP_Text m_LootboxAdded;
    public TMP_Text m_NoSlotsLeft;
    public TMP_Text m_LootboxNoSlots;
    public TMP_Text m_TapAnywhereHint;
    public GameObject m_LootboxPanel;
    public GameObject[] m_LootboxContainers;
    public Transform m_CenterContainer;
    public ParticleSystem m_LootboxGetFXPrefab;
    public SortingGroup m_NoRoomFXsortOrder;

    private JsonObject m_BonusLootboxContent;
    private bool m_IsOpen;
    private bool m_HasSlot;
    private LootboxInfo m_Lootbox;
    private LootboxAnimator m_LootboxAnimator;
    private float m_ChestScale;
    private const float m_ScaleFactor = 1.8f;
    private const float m_ChestFlyTime = .3f;
    private const float m_ChestAppearTime = .3f;
    private const float m_ChestFlyJumpTime = .6f;
    private const float m_ChestJumpTime = .75f;
    private const float m_ChestShakeTime = .75f;
    private const float m_PanelSlideTime = .4f;
    private const float m_ButtonBlinkTime = .75f;
    private const float m_ChestFXDuration = 1.5f;
    private const float m_ChestAnimDuration = 1.67f;
    private const float m_JumpPower = 5f;
    private const float m_ShakeStrength = 100f;
    private const float m_SoundFade = .5f;
    private const string m_DotweenAppear = "lootbox_appear";
    private const string m_DotweenCollect = "lootbox_collect";
    private const string m_DotweenReject = "lootbox_reject";

    private List<GameObject> m_Lootboxes = new List<GameObject>();


    /// <summary>
    /// Show lootbox window with 'open' button
    /// </summary>
    public void Show(LootboxInfo lootbox, LootboxAnimator lootboxAnimator, float scale, bool hasslot)
    {
        if (lootbox == null)
            return;

        Show();

        m_Lootbox = lootbox;
        // вычисляем цену открытия
        int instantOpenPrice = LootboxInfo.CalculateInstantOpenPrice(m_Lootbox.m_Data.OpenTime);

        // не отображаем цену, если ноль
        if (instantOpenPrice == 0)
        {
            m_NoPrice.gameObject.SetActive(true);
            m_CurrencyGroup.gameObject.SetActive(false);
        }
        else
        {
            m_NoPrice.gameObject.SetActive(false);
            m_CurrencyGroup.gameObject.SetActive(true);
            m_Currency.text = instantOpenPrice.ToString();
        }

        bool isOpened = m_Lootbox.GetState() == LootboxState.Opened;
        ShowLootbox(lootboxAnimator, scale, hasslot, isOpened);
    }

    ///////////////
    protected override void OnHide()
    {
        base.OnHide();

        m_BonusLootboxContent = null;
    }

    ///////////////
    public void ShowBonusLootbox(LootboxInfo lootbox, LootboxAnimator lootboxAnimator, float scale, JsonObject bonusLootboxInfo)
    {
        Show();

        m_Lootbox = lootbox;
        m_BonusLootboxContent = bonusLootboxInfo;

        m_NoPrice.gameObject.SetActive(true);
        m_CurrencyGroup.gameObject.SetActive(false);

        m_LootboxAdded.text = Localization.Get("bonus_chest_victory");

        ShowLootbox(lootboxAnimator, scale, true, true);
    }

    ///////////////
    private void ShowLootbox(LootboxAnimator lootboxAnimator, float scale, bool hasSlot, bool isOpened)
    {
        m_ChestScale = scale;
        m_HasSlot = hasSlot;
        m_IsOpen = isOpened;

        // нужно, чтобы срабатывал клик по области, прерывающий анимацию
        ActivateButtons(true);
        m_LootboxPanel.SetActive(false);
        m_AnchorCenter.gameObject.SetActive(false);
        m_LootboxAdded.gameObject.SetActive(hasSlot);
        m_LootboxNoSlots.gameObject.SetActive(!hasSlot);
        m_NoSlotsLeft.gameObject.SetActive(!hasSlot);
        m_TapAnywhereHint.gameObject.SetActive(isOpened);
        // получаем экземпляр анимации лутбокса
        m_LootboxAnimator = lootboxAnimator;
        m_LootboxAnimator.gameObject.SetActive(true);
        // выставляем его поверх сцены
        m_LootboxAnimator.m_SortingGroup.sortingOrder = SortingOrder + 1;
        m_LootboxAnimator.transform.SetParent(this.transform);
        // запоминаем его положение, чтобы потом вернуть обратно
        m_AnchorStart.position = m_LootboxAnimator.transform.position;

        // выставляем анимацию открытого лутбокса, если он доступен для открытия
        if (isOpened)
        {
            m_LootboxAnimator.SetOpened();
        }

        // проигрываем анимацию появления лутбокса
        Sequence sequence = LootboxAppear(m_AnchorCenter, m_ScaleFactor).SetId(m_DotweenAppear);

        if (isOpened)  // сундук бонусный и скаута открываем сразу
        {
            return;
        }

        if (hasSlot)
        {
            // есть доступный слот, сундук занимает его, и окно пропадает
            // анимацию можно прервать
            sequence.Append(CollectLootboxAnimated()).SetId(m_DotweenCollect);                
            return;
        }

        // доступных слотов нет; надо открыть сундук или потерять его
        if (!hasSlot)
        {
            m_Open.gameObject.SetActive(false);
            // анимацию можно прервать
            sequence.Append(LootboxRejectAnimation()).SetId(m_DotweenReject);
        }
    }

    /// <summary>
    /// Появление лутбокса
    /// </summary>
    private Sequence LootboxAppear(Transform end, float scale)
    {
        Transform target = m_LootboxAnimator.transform;
        target.position = end.position;
        Vector3 startsize = m_LootboxAnimator.transform.localScale;  // начальный размер
        Vector3 endsize = scale * startsize;
        Vector3 bigsize = endsize * 1.5f;   // увеличенный размер
        float halftime = m_ChestAppearTime / 2;

        Sequence sequence = DOTween.Sequence();
        sequence.Append(target.DOScale(bigsize, halftime))
                .Append(target.DOScale(endsize, halftime))
                .AppendCallback(() =>
                {
                    m_LootboxAnimator.transform.localScale = endsize;
                });

        return sequence;
    }

    /// <summary>
    /// Открываем лутбокс
    /// </summary>
    public void OnOpenButtonClick()
    {
        if (m_Lootbox != null)
        {
            if (m_BonusLootboxContent == null)
            {
                int instantOpenPrice = LootboxInfo.CalculateInstantOpenPrice(m_Lootbox.m_Data.OpenTime);
                // проверяем, достаточно ли у нас камней, чтобы открыть сундук
                // если нет, то откроется окно кассы с нотификацией
                if (!PlayerProfile.Instance.TrySpendGems(instantOpenPrice, true))
                    return;

                m_Lootbox.SetInstantlyOpened();
                // вызываем окно открытия лутбокса с выдачей награды
                UIManager.Instance.GetDialog<LootboxOpenDialog>().InstantVictoryOpen(m_Lootbox, instantOpenPrice);
            }
            else
            {
                UIManager.Instance.GetDialog<LootboxOpenDialog>().Show(m_Lootbox.m_Data.Id, m_BonusLootboxContent);
            }
        }

        Hide();
        UIManager.Instance.GetDialog<VictoryDialog>().OnLootboxOpen();
    }

    /// <summary>
    /// Действия при закрытии окна лутбокса
    /// </summary>
    public void OnCloseButtonClick()
    {
        if (DOTween.IsTweening(m_DotweenCollect))
        {
            DOTween.Complete(m_DotweenCollect, true);
            return;
        }

        if (DOTween.IsTweening(m_DotweenReject))
        {
            DOTween.Complete(m_DotweenReject, true);
            return;
        }

        if (DOTween.IsTweening(m_DotweenAppear))
        {
            DOTween.Complete(m_DotweenAppear, true);
        }

        if (m_IsOpen)
        {
            OnOpenButtonClick();
            return;
        }

        ActivateButtons(false);

        Sequence sequence = DOTween.Sequence();
        // При закрытии окна плавно перемещаем лутбокс в исходную позицию, одновременно меняя его размер
        // по завершении - закрываем диалог
        sequence.Append(m_LootboxAnimator.transform.DOMove(m_AnchorStart.position, m_ChestFlyTime).SetEase(Ease.InQuad))
                .Join(m_LootboxAnimator.transform.DOScale(m_LootboxAnimator.transform.localScale / m_ScaleFactor, m_ChestFlyTime).SetEase(Ease.InQuad));

        // выполняем цепочку
        StartCoroutine(ExecuteLockedSequence(sequence, () =>
        {
            Hide();
            UIManager.Instance.GetDialog<VictoryDialog>().OnLootboxReturned();
        }));
    }

    /// <summary>
    /// Анимация лутбокса, прыгающего в панель лутбоксов
    /// </summary>
    private Sequence CollectLootboxAnimated()
    {
        // нужно определить позицию в панели лутбоксов
        RectTransform placeHolder = PrepareLootboxPanelPosition();
        Vector3 endPos = placeHolder.position;
        Vector3 endSize = m_LootboxAnimator.transform.localScale;

        // добавить в цепочку показ панели лутбоксов (выезжает снизу)
        Sequence sequence = DOTween.Sequence().Append(LootboxPanelShow(true));

        // звуки
        sequence.InsertCallback(m_SoundFade, () =>
        {
            SoundController.Instance.PlaySound(SoundName.LootboxTakeSound);
        });

        // сундук отпрыгивает на свое место в панели, уменьшаясь в размере
        sequence.Append(m_LootboxAnimator.transform.DOJump(endPos, m_JumpPower, 1, m_ChestFlyJumpTime).SetEase(Ease.InQuad))
                .Join(m_LootboxAnimator.transform.DOScale(endSize, m_ChestFlyJumpTime).SetEase(Ease.InQuad))
                .AppendInterval(0.01f);

        ParticleSystem effectFX = null;
        // добавить эффект на лутбокс после прыжка и паузу для его проигрывания
        sequence.AppendCallback(() =>
        {
            effectFX = Instantiate(m_LootboxGetFXPrefab, m_LootboxAnimator.transform);
            effectFX.gameObject.SetActive(true);
            m_LootboxAnimator.m_Animator.StopPlayback();
            effectFX.Play(true);
        });
        sequence.AppendInterval(m_ChestFXDuration);

        // остальные лутбоксы в это время подпрыгивают на месте (асинхронно)
        foreach (var lootbox in m_Lootboxes)
        {
            sequence.Join(LootboxSmallJump(lootbox.transform));
        }

        // удаляем эффект после проигрывания и прикрепляем лутбокс к панели, чтобы он уезжал вместе со всеми
        sequence.AppendCallback(() =>
        {
            effectFX.Stop(true);
            Destroy(effectFX.gameObject);
            m_LootboxAnimator.transform.SetParent(placeHolder);
            placeHolder.gameObject.GetComponent<Image>().enabled = false;
            m_LootboxAnimator.m_Animator.StartPlayback();
        });
        // скрытие панели лутбоксов
        sequence.Append(LootboxPanelShow(false));

        sequence.OnComplete(() =>
        {
            Hide();
            UIManager.Instance.GetDialog<VictoryDialog>().OnLootboxReturned();
        });

        return sequence;
    }

    /// <summary>
    /// Анимация лутбокса, для которого нет места
    /// </summary>
    private Sequence LootboxRejectAnimation()
    {
        // дополнительная кнопка для создания blink-эффекта
        m_ButtonAdditive.gameObject.SetActive(false);

        Sequence sequence = DOTween.Sequence();

        // показываем панель лутбокосв
        PrepareLootboxPanelPosition();
        sequence.Append(LootboxPanelShow(true));

        // звуки
        sequence.InsertCallback(m_SoundFade, () =>
        {
            SoundController.Instance.PlaySound(SoundName.LootboxRejectSound);
        });

        // играем на лутбоксе анимацию "встряски"
        sequence.AppendCallback(() =>
        {
            m_AnchorCenter.gameObject.SetActive(true);
            m_LootboxAnimator.transform.localPosition = m_AnchorCenter.localPosition;
            m_LootboxAnimator.transform.SetParent(m_CenterContainer);
            // пыль должна быть поверх сундука
            int fxOrder = m_LootboxAnimator.m_SortingGroup.sortingOrder + 1;
            m_NoRoomFXsortOrder.sortingOrder = fxOrder;
        })
        .AppendInterval(m_ChestAnimDuration);

        // прячем панель лутбоксов, показываем кнопки
        sequence.Append(LootboxPanelShow(false))
                .AppendCallback(() => 
                {
                    ActivateButtons(true);
                    m_LootboxAnimator.transform.SetParent(transform);
                    m_LootboxAnimator.transform.localPosition = m_AnchorCenter.localPosition;
                    m_LootboxAnimator.transform.localRotation = transform.localRotation;
                });
        // кнопка открыть появляется из прозрачности
        sequence.Append(m_Open.image.DOFade(0f, 0.01f))
                .Append(m_Open.image.DOFade(1f, m_ButtonBlinkTime * 0.15f))
                .AppendInterval(m_ButtonBlinkTime * 0.28f);

        // включаем кнопку-накладку
        sequence.AppendCallback(() => 
        {
            m_ButtonAdditive.gameObject.SetActive(true);
        });
        Vector3 punchScale = new Vector3(m_Open.transform.localScale.x * 0.2f,
                        m_Open.transform.localScale.y * 0.2f,
                        m_Open.transform.localScale.z * 0.2f);
        // эффект желтой "вспышки" на кнопке
        sequence.Append(m_ButtonAdditive.DOFade(0f, 0.01f))
                .Append(m_Open.transform.DOPunchScale(punchScale, m_ButtonBlinkTime * 0.435f, 1, 0f).SetEase(Ease.InOutCubic))
                .Join(m_ButtonAdditive.transform.DOPunchScale(punchScale, m_ButtonBlinkTime * 0.435f, 1, 0f).SetEase(Ease.InOutCubic))
                .Join(DOTween.Sequence().Append(m_ButtonAdditive.DOFade(1f, m_ButtonBlinkTime * 0.11f).SetEase(Ease.InCubic))
                                        .Append(m_ButtonAdditive.DOFade(0f, m_ButtonBlinkTime * 0.46f).SetEase(Ease.OutCubic))
                                        // прячем кнопку-накладку
                                        .AppendCallback(() => 
                                        {
                                            m_ButtonAdditive.gameObject.SetActive(false);
                                        }));

        return sequence;
    }

    /// <summary>
    /// Включить/отключить кнопки в диалоге
    /// </summary>
    private void ActivateButtons(bool flag)
    {
        if (TutorialManager.Instance.IsActive())
        {
            // в режиме тутора кнопка открытия скрыта всегда
            m_Open.gameObject.SetActive(false);
        }
        else
        {
            if (m_IsOpen || m_HasSlot)
            {
                m_Open.gameObject.SetActive(false);
            }
            else
            {
                m_Open.gameObject.SetActive(flag);
            }
        }

        m_Frame.SetActive(flag);
    }

    /// <summary>
    /// Выполняем цепочку твинов в режиме залочки диалога, передаем экшн
    /// </summary>
    private IEnumerator ExecuteLockedSequence(Sequence sequence, Action callback)
    {
        LockDialog();

        yield return sequence.WaitForCompletion();

        UnlockDialog();

        if (callback != null)
            callback();
    }

    /// <summary>
    /// Подготавливаем к показу панель лутбоксов, возвращаем позицию нашего лутбокса
    /// </summary>
    private RectTransform PrepareLootboxPanelPosition()
    {
        foreach (LootboxInfo lootbox in PlayerProfile.Instance.Lootboxes)
        {
            if (lootbox != null && lootbox != m_Lootbox)
            {
                LootboxData lootboxData = lootbox.m_Data;
                // создаем анимированный лутбокс и ставим его в начальную позицию, а также скейлим
                LootboxAnimator prefab = ResourcesHelper.GetResource<LootboxAnimator>(lootboxData.Prefab);
                LootboxAnimator newbox = Instantiate(prefab);
                newbox.m_SortingGroup.sortingOrder = SortingOrder + 1;
                float scale = lootboxData.UiScale * m_ChestScale;
                newbox.transform.localScale = new Vector3(scale, scale, scale);
                // позиционируем поверх контейнера
                newbox.transform.position = m_LootboxContainers[lootbox.m_SlotNum].transform.position;
                // убираем тень под сундуком
                m_LootboxContainers[lootbox.m_SlotNum].GetComponent<Image>().enabled = false;
                newbox.gameObject.SetActive(true);
                // привязываем к панели, чтобы лутбокс ездил вместе с ней
                newbox.transform.SetParent(m_LootboxContainers[lootbox.m_SlotNum].transform);
                m_Lootboxes.Add(newbox.gameObject);
            }
        }

        int slot = m_Lootbox.m_SlotNum;

        if (slot >= 0 && slot < Constants.MaxLootboxesSlots)
        {
            return m_LootboxContainers[slot].GetComponent<RectTransform>();
        }
        else
            return null;
    }

    /// <summary>
    /// Показать/скрыть панель лутбоксов
    /// </summary>
    private Sequence LootboxPanelShow(bool show)
    {
        // вычисляем размеры панели и необходимое смещение за пределы экрана
        Rect dialogRect = gameObject.GetComponent<RectTransform>().rect;
        Rect panelRect = m_LootboxPanel.GetComponent<RectTransform>().rect;
        float bottomOnScreen = panelRect.height / 2 - dialogRect.height / 2;
        float offsetY = dialogRect.height / 2 + panelRect.height / 2;

        // учитываем IPhoneX SafeArea
        offsetY += Helper.GetScreenSafeArea().position.y;
        float bottomOffScreen = -(offsetY + 100f);

        Vector3 onScreenPos = m_LootboxPanel.transform.localPosition;
        onScreenPos.y = bottomOnScreen;
        Vector3 offScreenPos = m_LootboxPanel.transform.localPosition;
        offScreenPos.y = bottomOffScreen;
        Vector3 dest;

        if (show)
        {
            // если показываем панель, она должна выезжать из-за нижней границы на свое место
            m_LootboxPanel.transform.localPosition = offScreenPos;
            dest = onScreenPos;
        }
        else
        {
            // если прячем панель, она уезжает с текущего положения за нижнюю границу
            dest = offScreenPos;
        }

        m_LootboxPanel.SetActive(true);

        Sequence sequence = DOTween.Sequence();
        sequence.Append(m_LootboxPanel.transform.DOLocalMove(dest, m_PanelSlideTime))
                .AppendCallback(() =>
                {
                    if (show)
                        return;

                    foreach (var lootbox in m_Lootboxes)
                        Destroy(lootbox);

                    m_Lootboxes.Clear();
                    m_LootboxPanel.SetActive(false);
                    m_LootboxPanel.transform.localPosition = onScreenPos;
                });

        return sequence;
    }

    /// <summary>
    /// Небольшое подпрыгивание с наклонами из стороны в сторону
    /// </summary>
    private Sequence LootboxSmallJump(Transform lootbox)
    {      
        Vector3 pos = Vector3.zero;
        // задаем случайные значения тайминга, амплитуды прыжка и углов наклона
        pos.y = lootbox.localPosition.y * (-1) * UnityEngine.Random.Range(0.2f, 0.4f);
        Vector3 rot = Vector3.zero;
        bool plus = UnityEngine.Random.Range(0, 2) == 1;
        rot.z = UnityEngine.Random.Range(6f, 10f) * (plus ? 1f : -1f);
        float delay = UnityEngine.Random.Range(0.01f, m_ChestJumpTime / 4f);

        return DOTween.Sequence()
            .SetDelay(delay)
            .Append(lootbox.DOPunchPosition(pos, m_ChestJumpTime / 2f - delay, 1, 0f).SetEase(Ease.InOutBack))
            .Join(lootbox.DOPunchRotation(rot, m_ChestJumpTime / 2f - delay).SetEase(Ease.InOutCubic));
    }
}
