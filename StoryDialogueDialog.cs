using System.Collections;
using System.Collections.Generic;
using TMPro;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;
using System;

// экранный персонаж со своим портретом
public class StoryCharacter
{
    public string m_Name;
    private Image m_Portrait;  // задает положение на экране
    private Sprite m_Sprite;   // картинка персонажа
    private StoryDialogueDialog m_Dialog;

    public bool IsOnScreen { get; private set; }
    public bool IsActive { get; private set; }
    public bool IsStay { get; private set; }    // остается на экране или скрывается после своей реплики
    public enum ScreenSide
    {
        left,
        right
    };
    public ScreenSide ScreenPos { get; private set; }

    private const string StoryDialogTweenId = "storyDialogTweenId";

    ///////////////
    public StoryCharacter()
    {
        m_Name = string.Empty;
        m_Sprite = null;
        IsOnScreen = false;
        IsActive = true;
    }

    ///////////////
    public void Setup(string name, Sprite sprite, bool willStay, StoryDialogueDialog dialog)
    {
        m_Name = name;
        m_Sprite = sprite;
        m_Dialog = dialog;
        IsStay = willStay;
    }

    /// <summary>
    /// Появление персонажа ("выезд") из-за границ экрана
    /// </summary>
    public Sequence Appear(ScreenSide side, float deltaTime, float delay = 0f)
    {
        float localPositionX = 0f;
        ScreenPos = side;
        Vector3 pos;

        switch (ScreenPos)
        {
            case ScreenSide.left:
                // выезд слева
                m_Portrait = m_Dialog.m_LeftPortrait;
                pos = m_Portrait.transform.localPosition;
                m_Portrait.transform.localPosition = new Vector3(StoryDialogueDialog.m_LeftOffScreen, pos.y, pos.z);
                localPositionX = StoryDialogueDialog.m_LeftPortraitPos;
                break;

            case ScreenSide.right:
                // выезд справа
                m_Portrait = m_Dialog.m_RightPortrait;
                pos = m_Portrait.transform.localPosition;
                m_Portrait.transform.localPosition = new Vector3(StoryDialogueDialog.m_RightOffScreen, pos.y, pos.z);

                localPositionX = StoryDialogueDialog.m_RightPortraitPos;
                break;

            default:
                break;
        }

        Sequence sequence = DOTween.Sequence().AppendInterval(delay);

        sequence.SetId(StoryDialogTweenId);

        if (m_Sprite != null)
        {
            m_Portrait.gameObject.SetActive(true);
            m_Portrait.overrideSprite = m_Sprite;

            if (!IsActive)
            {
                sequence.Append(SetActive(true, 0f));
            }

            sequence.Append(m_Portrait.transform.DOLocalMoveX(localPositionX, deltaTime).SetEase(Ease.OutQuad));
        }
        else
            m_Portrait.gameObject.SetActive(false);

        IsOnScreen = true;
        // добавляем перемещение таблички с именем одновременно с появлением персонажа
        sequence.Join(NameBackReplace(localPositionX, deltaTime / 2f));

        return sequence;
    }

    /// <summary>
    /// Персонаж скрывается ("уезжает") за границы экрана
    /// </summary>
    public Sequence Disappear(float deltaTime, float delay = 0f)
    {
        float localPositionX = 0f;

        switch (ScreenPos)
        {
            case ScreenSide.left:
                m_Portrait = m_Dialog.m_LeftPortrait;
                localPositionX = StoryDialogueDialog.m_LeftOffScreen;
                break;

            case ScreenSide.right:
                m_Portrait = m_Dialog.m_RightPortrait;
                localPositionX = StoryDialogueDialog.m_RightOffScreen;
                break;

            default:
                break;
        }

        Sequence sequence = DOTween.Sequence().AppendInterval(delay);

        sequence.SetId(StoryDialogTweenId);

        if (m_Portrait.overrideSprite != null)
        {
            m_Portrait.gameObject.SetActive(true);
            sequence.Append(m_Portrait.transform.DOLocalMoveX(localPositionX, deltaTime).SetEase(Ease.InQuad));

            if (!IsActive)
            {
                sequence.Append(SetActive(true, 0f));
            }
        }
        else
            m_Portrait.gameObject.SetActive(false);

        m_Dialog.SetTitle(string.Empty);
        IsOnScreen = false;

        return sequence;
    }

    /// <summary>
    /// Перемещение таблички с именем активного персонажа в позицию под его портретом
    /// </summary>
    public Sequence NameBackReplace(float posx, float deltaTime)
    {
        m_Dialog.SetTitle(m_Name);

        RectTransform nameRectTransform = m_Dialog.m_Title.transform.parent.GetComponent<RectTransform>();
        Vector2 nameBackPos = nameRectTransform.anchoredPosition;
        nameBackPos.x = posx;

        Sequence sequence = DOTween.Sequence().Append(nameRectTransform.DOAnchorPos(nameBackPos, deltaTime).SetEase(Ease.OutQuint));

        sequence.SetId(StoryDialogTweenId);

        return sequence;
    }

    /// <summary>
    /// Персоенаж становится активным/неактивным (уменьшение в размерах и затемнение)
    /// </summary>
    public Sequence SetActive(bool active, float deltaTime, float delay = 0f)
    {
        if (!IsOnScreen)
            return null;

        Vector3 scale;
        Color color;

        if (!active)
        {
            // уменьшение в размере и затемнение
            scale = new Vector3(0.9f, 0.9f, 0.9f);
            color = new Color32(128, 128, 128, 255);
        }
        else
        {
            scale = new Vector3(1, 1, 1);
            color = new Color32(255, 255, 255, 255);
        }

        if (ScreenPos == ScreenSide.right)
        {
            // для правой стороны нужно зеркальное отображение картинки
            scale.x = -scale.x;
        }

        Sequence sequence = DOTween.Sequence().AppendInterval(delay);

        sequence.SetId(StoryDialogTweenId);

        if (m_Portrait.overrideSprite != null)
        {
            m_Portrait.gameObject.SetActive(true);
            sequence.Append(m_Portrait.DOColor(color, deltaTime))
                    .Join(m_Portrait.rectTransform.DOScale(scale, deltaTime));
        }
        else
            m_Portrait.gameObject.SetActive(false);

        IsActive = active;
        float posx;

        if (active && deltaTime > 0f)
        {
            if (ScreenPos == ScreenSide.left)
            {
                posx = StoryDialogueDialog.m_LeftPortraitPos;
            }
            else
            {
                posx = StoryDialogueDialog.m_RightPortraitPos;
            }

            sequence.Join(NameBackReplace(posx, deltaTime));
        }

        return sequence;
    }
}

public class StoryDialogueDialog : BaseDialog
{
    public Image m_LeftPortrait;
    public Image m_RightPortrait;
    public Image m_StoryBar;
    public Transform m_ShadowTop;
    public Transform m_ShadowBottom;
    public TMP_Text m_Text;
    public TMP_Text m_Title;
    public Button m_SkipDialogStepButton;

    public const float m_LeftPortraitPos = -635f;
    public const float m_RightPortraitPos = 635f;
    public static float m_StoryBarPos;
    public static float m_LeftOffScreen;
    public static float m_RightOffScreen;
    public static float m_BottomOffScreen;

    private const float m_PortraitWidth = 650f;
    private const float m_ScrollHeight = 375f;
    private const float m_SlideTimeSide = .5f;
    private const float m_SlideTimeBottom = .3f;
    private const float m_SlideTimeShadow = .45f;
    private const float m_TextAnimationDelay = .01f;

    private StoryDialogueData m_CurrentDialogueData;
    private List<StoryCharacter> m_Characters = new List<StoryCharacter>();
    private Coroutine m_TextAnimation;
    private bool m_IsHUDConcealed;

    private const string StoryDialogTweenId = "storyDialogTweenId";

    ///////////////
    public void Show(StoryDialogueData data)
    {
        Rect dialogRect = gameObject.GetComponent<RectTransform>().rect;
        float offsetX = dialogRect.width / 2 + m_PortraitWidth / 2;
        float offsetY = dialogRect.height / 2 + m_ScrollHeight / 2;
        m_StoryBarPos = m_ScrollHeight / 2 - dialogRect.height / 2;

        // для IPhoneX - добавляем смещение до safe area
        Vector2 safeRect = Helper.GetScreenSafeArea().position;
        offsetX += safeRect.x;
        offsetY += safeRect.y;

        // устанавливаем координаты off screen для анимаций
        m_RightOffScreen = offsetX;
        m_LeftOffScreen = -offsetX;
        m_BottomOffScreen = -offsetY;

        Show();
        
        m_CurrentDialogueData = data;
        m_Characters.Clear();

        m_Title.text = string.Empty;
        m_Text.text = string.Empty;
        m_RightPortrait.gameObject.SetActive(false);
        m_LeftPortrait.gameObject.SetActive(false);
        m_TextAnimation = null;
        m_IsHUDConcealed = false;

        AnimateShow(() => UpdateView());
    }

    ///////////////
    private void UpdateView()
    {
        if (m_CurrentDialogueData == null)
        {
            return;
        }

        string name = Localization.Get(m_CurrentDialogueData.GetUnitName());
        bool isLeft = m_CurrentDialogueData.IsUnitOnLeftSide;
        StoryCharacter.ScreenSide screenSide = isLeft ? StoryCharacter.ScreenSide.left : StoryCharacter.ScreenSide.right;
        StoryCharacter showPerson = null;     // активный персонаж
        StoryCharacter replacePerson = null;  // персонаж, который должен уступить место на экране активному
        StoryCharacter inactivePerson = null; // персонаж, который должен стать неактивным и остаться на экране

        foreach (var character in m_Characters)
        {
            if (character.m_Name.Equals(name))
            {
                showPerson = character;
            }
            else if (character.ScreenPos == screenSide && character.IsOnScreen)
            {
                replacePerson = character;
            }
            else if (character.ScreenPos != screenSide && character.IsOnScreen && character.IsActive)
            {
                inactivePerson = character;
            }
        }

        if (showPerson == null)
        {
            // настройка активного персонажа
            showPerson = new StoryCharacter();
            m_Characters.Add(showPerson);
        }

        // настройка персонажа может поменяться от шага к шагу
        showPerson.Setup(name, m_CurrentDialogueData.GetUnitPortrait(), m_CurrentDialogueData.StayOnScene, this);

        Sequence sequence = DOTween.Sequence();

        sequence.SetId(StoryDialogTweenId);

        if (replacePerson != null)
        {
            // убираем того, который должен уйти
            sequence.Append(replacePerson.Disappear(m_SlideTimeSide));
        }

        if (showPerson.IsOnScreen && showPerson.ScreenPos != screenSide)
        {
            // одновременно с этим убираем активного, если он до этого был на экране с другой стороны
            sequence.Join(showPerson.Disappear(m_SlideTimeSide));
        }

        StartCoroutine(ExecuteLockedSequence(sequence, true, () =>
        {
            Sequence sequence1 = DOTween.Sequence();

            sequence1.SetId(StoryDialogTweenId);

            if (!showPerson.IsOnScreen)
            {
                // показываем персонажа...
                sequence1.Append(showPerson.Appear(screenSide, m_SlideTimeSide));
            }
            else if (!showPerson.IsActive)
            {
                // ...или делаем его активным, если он затемнен
                sequence1.Append(showPerson.SetActive(true, m_SlideTimeSide));
            }

            if (inactivePerson != null)
            {
                if (inactivePerson.IsStay)
                {
                    // неактивного персонажа, если стоит флаг stay_on_scene, затемняем
                    sequence1.Join(inactivePerson.SetActive(false, m_SlideTimeSide));
                }
                else
                {
                    // иначе убираем со сцены
                    sequence1.Join(inactivePerson.Disappear(m_SlideTimeSide));
                }
            }

            m_Text.text = string.Empty;

            StartCoroutine(ExecuteLockedSequence(sequence1, true, () =>
            {
                if (m_TextAnimation != null)
                {
                    StopCoroutine(m_TextAnimation);
                    m_TextAnimation = null;
                }

                // после выполнения всех анимаций запускаем анимацию текста в окне диалога
                m_TextAnimation = StartCoroutine(AnimateText(Localization.Get(m_CurrentDialogueData.Text)));
            }));
        }));
    }

    ///////////////
    public void SetTitle(string name)
    {
        m_Title.text = name;
    }

    ///////////////
    public void OnClickSkip()
    {
        HideStory();
    }

    ///////////////
    public void OnClickNext()
    {
        if (FinishTextAnimation())
            return;

        if (m_CurrentDialogueData == null)
        {
            HideStory();
            return;
        }
        
        m_CurrentDialogueData = StoryDialoguesDataStorage.Instance.GetById(m_CurrentDialogueData.NextDialogueId);

        if (m_CurrentDialogueData != null)
        {
            UpdateView();
            return;
        }

        HideStory();
    }

    ///////////////
    private void HideStory()
    {
        FinishTextAnimation();
        AnimateHide(() => Hide());
    }

    ///////////////
    protected override void OnHide()
    {
        base.OnHide();

        TutorialManager.Instance.UpdateStep(TutorialStepType.ShowStoryDialogue);
    }

    /// <summary>
    /// Действия при показе диалога
    /// </summary>
    private void AnimateShow(Action onComplete)
    {
        Sequence sequence = DOTween.Sequence();

        sequence.SetId(StoryDialogTweenId);
        // показываем "шторки"
        sequence.Append(ShadowBarsScale(true, m_SlideTimeShadow));

        if (UIManager.Instance.NeedHideHUDAnimated())
        {
            // если нужно скрыть HUD - убираем интерфейс
            sequence.Join(UIManager.Instance.HideHUDAnimated());
            m_IsHUDConcealed = true;

            if (CastleCanvas.Instance != null)
                // также на сцене замка убираем лутбоксы
                sequence.Join(CastleCanvas.Instance.m_LootboxPanel.HideLootboxesAnimated());
        }

        // выезжает панель диалога, сразу вслед за шторками
        sequence.Join(StoryBarSlide(true, m_SlideTimeBottom, m_SlideTimeShadow));
        // при старте диалога мы разрешаем Skip all прерывать анимацию (параметр "псевдолок")
        StartCoroutine(ExecuteLockedSequence(sequence, true, onComplete));   
    }

    /// <summary>
    /// Действия при скрытии диалога
    /// </summary>
    private void AnimateHide(Action onComplete)
    {
        DOTween.Kill(StoryDialogTweenId);  // обрываем все активные анимации, чтобы не ждать, пока они доиграют

        Sequence sequence = DOTween.Sequence();

        foreach (var person in m_Characters)
        {
            // вначале убираем со сцены всех персонажей
            if (person != null && person.IsOnScreen)
            {
                sequence.Join(person.Disappear(m_SlideTimeSide));
            }
        }

        // последовательно убираем панель диалога, затем шторки, и показываем интерфейс
        sequence.Append(StoryBarSlide(false, m_SlideTimeBottom))
                .Append(ShadowBarsScale(false, m_SlideTimeShadow));

        if (m_IsHUDConcealed)
        {
            // если прятали интерфейс, надо показать
            m_IsHUDConcealed = false;
            sequence.Join(UIManager.Instance.ShowHUDAnimated());

            if (CastleCanvas.Instance != null)
                sequence.Join(CastleCanvas.Instance.m_LootboxPanel.ShowLootboxesAnimated());
        }

        StartCoroutine(ExecuteLockedSequence(sequence, false, onComplete));
    }

    /// <summary>
    /// Показываем/прячем панель диалога
    /// </summary>
    private Sequence StoryBarSlide(bool onScreen, float deltaTime, float delay = 0f)
    {
        Sequence sequence = DOTween.Sequence().AppendInterval(delay);

        sequence.SetId(StoryDialogTweenId);
        Vector3 pos = m_StoryBar.transform.localPosition;

        // панель должна сделать небольшое обратное движение перед основным мувом;
        // подсчитываем время на это движение (punchTime), исходя из скорости, с которой совершается основное перемещение
        float punchDelta = 20f;
        float speedFactor = 10f;
        float velocity = (Mathf.Abs(m_BottomOffScreen) - Mathf.Abs(m_StoryBarPos)) / m_SlideTimeBottom;
        Vector3 punchVector = new Vector3(0, punchDelta, 0);
        float punchTime = (punchDelta * 2 / velocity) * speedFactor;

        if (onScreen)
        {
            // панель выезжает снизу
            Vector3 newpos = new Vector3(pos.x, m_StoryBarPos, pos.z);
            m_StoryBar.transform.localPosition = new Vector3(pos.x, m_BottomOffScreen, pos.z);

            sequence.Append(m_StoryBar.transform.DOLocalMove(newpos + punchVector, deltaTime).SetEase(Ease.OutQuad))
                    .Append(m_StoryBar.transform.DOLocalMove(newpos, punchTime));
        }
        else
        {
            // панель уезжает с экрана вниз
            Vector3 newpos = new Vector3(pos.x, m_BottomOffScreen, pos.z);
            m_StoryBar.transform.localPosition = new Vector3(pos.x, m_StoryBarPos, pos.z);

            sequence.Append(m_StoryBar.transform.DOLocalMove(pos + punchVector, punchTime))
                    .Append(m_StoryBar.transform.DOLocalMove(newpos, deltaTime).SetEase(Ease.InQuad));
        }

        return sequence;
    }

    /// <summary>
    /// Опускаем/поднимаем "шторки" - темные полосы вверху и внизу экрана
    /// </summary>
    private Sequence ShadowBarsScale(bool onScreen, float deltaTime)
    {
        Vector3 scale;

        if (onScreen)
        {
            // когда шторки опускаются - они скейлятся по Y от 0 к 1
            scale = new Vector3(1, 1, 1);
            m_ShadowTop.localScale = new Vector3(1, 0, 1);
            m_ShadowBottom.localScale = new Vector3(1, 0, 1);
        }
        else
        {
            // когда поднимаются - скейлятся обратно в ноль
            scale = new Vector3(1, 0, 1);
            m_ShadowTop.localScale = new Vector3(1, 1, 1);
            m_ShadowBottom.localScale = new Vector3(1, 1, 1);
        }

        return DOTween.Sequence().Append(m_ShadowTop.transform.DOScale(scale, deltaTime))
            .Join(m_ShadowBottom.transform.DOScale(scale, deltaTime));
    }

    /// <summary>
    /// Выполняет цепочку анимаций с залочкой диалога, чтобы нельзя было прервать анимации тапом
    /// Псевдолок позволяет лочить не весь диалог, а только кнопку Next
    /// </summary>
    private IEnumerator ExecuteLockedSequence(Sequence sequence, bool pseudoLock, Action onComplete)
    {
        if (sequence == null || sequence.IsComplete())
            yield break;

        LockDialog(pseudoLock);

        yield return sequence.WaitForCompletion(true);

        UnlockDialog(pseudoLock);

        if (onComplete != null)
            onComplete();
    }

    ///////////////
    private void LockDialog(bool pseudoLock)
    {
        if (pseudoLock)
            m_SkipDialogStepButton.interactable = false;
        else
            base.LockDialog();
    }

    ///////////////
    private void UnlockDialog(bool pseudoLock)
    {
        if (pseudoLock)
            m_SkipDialogStepButton.interactable = true;
        else
            base.UnlockDialog();
    }

    /// <summary>
    /// Анимированный вывод текста в панели диалога
    /// </summary>
    private IEnumerator AnimateText(string text)
    {
        m_Text.text = text;
        m_Text.ForceMeshUpdate();
        int totalChars = m_Text.textInfo.characterCount;
        int counter = 0;

        while (counter < totalChars)
        {
            counter++;
            m_Text.maxVisibleCharacters = counter;
            yield return new WaitForSeconds(m_TextAnimationDelay);
        }

        m_TextAnimation = null;
    }

    /// <summary>
    /// Прерываем анимацию текста и сразу выводим весь текст
    /// </summary>
    private bool FinishTextAnimation()
    {
        if (m_TextAnimation != null)
        {
            StopCoroutine(m_TextAnimation);
            m_TextAnimation = null;
            m_Text.maxVisibleCharacters = m_Text.textInfo.characterCount;
            return true;
        }

        return false;
    }
}
