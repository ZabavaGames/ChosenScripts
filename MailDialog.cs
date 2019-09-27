using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class MailDialog : BaseDialog
{
    public MailItem m_MailItemPrefab;
    public ScrollRect m_ScrollRect;
    public MailToolsPanel m_ToolsPanel;
    
    public readonly List<MailItem> m_ScrollItems = new List<MailItem>();

    private const int m_PageSize = 20;
    private const float m_PagingDelta = 100f;
    private int m_TotalActiveScrollItems;
    private int m_ScrollPosition;
    private bool m_PagingFlag;


    /////////////////
    private void OnEnable()
    {
        MailManager.OnMailUpdated += InitView;
        MailItem.OnClickEvent += OnClickScrollItem;
        MailItem.OnSelectEvent += OnMailItemSelected;
    }

    ///////////////
    private void OnDisable()
    {
        MailManager.OnMailUpdated -= InitView;
        MailItem.OnClickEvent -= OnClickScrollItem;
        MailItem.OnSelectEvent -= OnMailItemSelected;
    }

    ///////////////
    protected override void OnShow()
    {
        base.OnShow();

        m_PagingFlag = false;
        m_ScrollPosition = 1;

        m_ScrollRect.content.gameObject.SetActive(false);
        m_ToolsPanel.gameObject.SetActive(false);

        MailManager.Instance.TryUpdateMail();
        ShowLoadingIndicator();
        LockDialog();
    }

    ///////////////
    private void InitView()
    {
        HideLoadingIndicator();
        UnlockDialog();
        
        m_ScrollRect.content.gameObject.SetActive(true);
        RefreshScroll();
    }

    ///////////////
    public void RefreshScroll()
    {
        if (!IsShown())
            return;

        // получаем актуальные данные по письмам
        Dictionary<string, MailInfo> data = MailManager.Instance.GetMailContent();

        int itemIndex = 0;

        // формируем список элементов скролла
        foreach (var mailInfo in data)
        {
            MailItem scrollItem = GetOrCreateScrollItem(itemIndex);
            scrollItem.gameObject.SetActive(true);
            scrollItem.SetInfo(mailInfo.Value, itemIndex);
            itemIndex++;
        }

        m_TotalActiveScrollItems = itemIndex;
        
        // выставляем размер скролла по количеству элементов
        if (itemIndex > 0)
        {
            m_ScrollRect.enabled = true;
            VerticalLayoutGroup scrollLayout = m_ScrollRect.content.GetComponent<VerticalLayoutGroup>();
            float sizeDelta = m_MailItemPrefab.GetComponent<RectTransform>().sizeDelta.y;
            float height = itemIndex * sizeDelta;
            height += (itemIndex - 1) * scrollLayout.spacing;
            height += scrollLayout.padding.top + scrollLayout.padding.bottom;

            m_ScrollRect.content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            m_ScrollRect.verticalNormalizedPosition = 1;
            m_ScrollRect.StopMovement();

            if (m_PagingFlag)
            {
                // при пейджинге пролистываем одну страницу в скролле таким образом, чтобы последняя строчка на экране оказалась первой
                Vector2 pos1 = m_ScrollRect.content.localPosition;
                Vector2 shiftedPos = m_ScrollRect.content.GetChild(m_ScrollPosition - 1).position;
                Vector2 pos2 = m_ScrollRect.transform.InverseTransformPoint(shiftedPos);
                Vector2 delta = new Vector2(0, sizeDelta / 2 + scrollLayout.padding.top);
                m_ScrollRect.content.anchoredPosition = pos1 - pos2 - delta;
            }
        }
        else
        {
            m_ScrollRect.enabled = false;
        }
        
        // скрываем лишние элементы в скролле
        for (; itemIndex < m_ScrollItems.Count; itemIndex++)
        {
            m_ScrollItems[itemIndex].gameObject.SetActive(false);
        }

        m_PagingFlag = false;
    }

    ///////////////
    private MailItem GetOrCreateScrollItem(int index)
    {
        if (index < m_ScrollItems.Count)
            return m_ScrollItems[index];

        MailItem scrollItem = Instantiate(m_MailItemPrefab, m_ScrollRect.content);
        m_ScrollItems.Add(scrollItem);

        return scrollItem;
    }

    ///////////////
    private void OnClickScrollItem(MailItem item)
    {
        DeselectScrollItems();
        m_ToolsPanel.gameObject.SetActive(false);
        
        UIManager.Instance.GetDialog<MailOpenDialog>().Show(m_ScrollItems, item.Index, m_TotalActiveScrollItems);
    }

    ///////////////
    private void OnMailItemSelected()
    {
        // проверка, что выделено хотя бы одно письмо
        bool hasSelectedItems = false;

        foreach (var item in m_ScrollItems)
        {
            if (!item.gameObject.activeSelf)
                break;

            if (item.m_SelectToggle.isOn)
            {
                hasSelectedItems = true;
                break;
            }
        }

        if (!hasSelectedItems)
        {
            m_ToolsPanel.gameObject.SetActive(false);
            return;
        }
        
        if (m_ToolsPanel.gameObject.activeSelf)
            return;
        
        // если было выбрано письмо, то надо отобразить панель инструментов
        
        m_ToolsPanel.gameObject.SetActive(true);
    }

    ///////////////
    public void DeselectScrollItems()
    {
        foreach (var item in m_ScrollItems)
        {
            if (!item.gameObject.activeSelf)
                break;

            item.m_SelectToggle.isOn = false;
        }
    }

    /// <summary>
    /// Коллбек на завершение тача при скролле
    /// </summary>
    public void OnEndScrollDrag()
    {
        if (!m_ScrollRect.isActiveAndEnabled)
            return;

        if (MailManager.Instance.TotalCounter <= m_PageSize)
            return;

        if (MailManager.Instance.TotalCounter <= m_TotalActiveScrollItems)
            return;

        if (m_ScrollRect.verticalNormalizedPosition > 0f)
            return;

        // если крутит дальше, выполняем пагинацию
        float delta = Mathf.Abs(m_ScrollRect.verticalNormalizedPosition * m_ScrollRect.content.localPosition.y);

        if (delta > m_PagingDelta)
        {
            if (m_PagingFlag)
                return;

            m_PagingFlag = true;
            m_ScrollPosition = m_TotalActiveScrollItems;

            m_ToolsPanel.gameObject.SetActive(false);
            ShowLoadingIndicator();
            LockDialog();

            long lastSeq = m_ScrollItems[m_TotalActiveScrollItems - 1].Info.m_Seq;
            MailManager.Instance.TryUpdateMail(lastSeq);
        }
    }
}
