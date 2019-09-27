using System;
using System.Collections;
using System.Collections.Generic;
using MonsterLove.StateMachine;
using Pathfinding.RVO;
using Spine.Unity;
using Test;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

public class AnimationTest : MonoBehaviour
{
    public BattleEnvironment m_Environment;
    public Text m_HitLabel;
    public Text m_HasVectorBoneLabel;
    public Dropdown m_DropdownAnimations;
    public Dropdown m_DropdownUnits;
    public GameObject m_AnimationViewerGroup;
    public Button m_TestMoveBtn;
    public Slider m_AttackSpeedSlider;
    public TMP_Text m_AttackSpeedText;
    public Slider m_UnitScaleSlider;
    public TMP_Text m_UnitScaleText;
    [Header("debug fx per-unit")]
    // для отладки эффектов per-unit
    public GameObject m_TestFxView;
    public GameObject m_FxTestPrefab;
    [Tooltip("Название анимации из админки 'animation'")]
    public string m_FxAnimationName;
    public TestSpineBoneFx m_TestSpineBoneFx;
    [Header("attack_box settings")] 
    public float m_AttackBoxWidth;
    public float m_AttackBoxHeight;
    public UnitRadius m_ColliderRadius;
    
    [Space(20)]
    //фильтры
    public Toggle m_SoldiersToggle;
    public Toggle m_HeroesToggle;
    public Toggle m_CommonEnemiesToggle;
    public Toggle m_BossesToggle;
    public Toggle m_BigBossesToggle;
    public Dropdown m_RaceFilter;

    private GameObject m_ActiveUnit;
    private UnitAnimator m_ActiveUnitAnimator;
    private Coroutine m_HideHitTextAnimation;
    private Color m_PrevHit2Color = Color.green;
    private List<UnitData> m_DropdownUnitsData = new List<UnitData>();
    private bool m_IsUnitDead;
    private GameObject m_ActiveTargetUnit;
    private bool m_HasVectorBone;
    private AudioSource m_AudioSource;

    public enum TestSpineBoneFx
    {
        root,
        fx_head,
        hit_effect
    }
    
    /// <summary>
    /// Handles OnEnable callback
    /// </summary>
    private void OnEnable()
    {
        EventHandlerTest.OnHitEvent += OnAttackHitEvent;
        m_TestFxView.SetActive(Application.isEditor);
    }

    /// <summary>
    /// Handles OnDisable
    /// </summary>
    private void OnDisable()
    {
        EventHandlerTest.OnHitEvent -= OnAttackHitEvent;
    }

    /// <summary>
    /// Handles Start
    /// </summary>
    private void Start()
    {
        // min - по GDD (см. код Unit.cs)
        m_AttackSpeedSlider.minValue = 0.25f;
        m_AttackSpeedSlider.maxValue = 3f;

        m_UnitScaleSlider.minValue = .5f;
        m_UnitScaleSlider.maxValue = 2f;
        
        m_Environment.SetupBackground(BattleBackgroundType.Default);
        InitDropdowns();
        InitRaceFilter();
    }

    ///////////////
    private void Update()
    {   
        if (!m_HasVectorBone || Helper.IsPointerOverGameObject() || !m_AnimationViewerGroup.activeSelf)
            return;
        
        if (!Input.GetMouseButtonUp(0))
            return;

        Vector3 touchPosition = Input.mousePosition;
        Ray ray = Camera.main.ScreenPointToRay(touchPosition);

        RaycastHit[] hits = Physics.RaycastAll(ray, Mathf.Infinity);

        for (int i = 0; i < hits.Length; i++)
        {
            var hit = hits[i];

            // "костыльное" тестирование наведения оружия, чтобы не лезть в логику юнита
            if (hit.collider != null)
            {
                m_ActiveTargetUnit.SetActive(true);
                m_ActiveTargetUnit.transform.position = hit.point;
                
                m_ActiveUnit.GetComponent<UnitTest>().LookAt(m_ActiveTargetUnit.transform.position, m_ActiveUnitAnimator);
                return;
            }
        }
    }

    /// <summary>
    /// Handles click on exit button
    /// </summary>
    public void OnClickExit()
    {
        UIManager.Instance.LoadScene(SceneName.Castle);
    }
    
    /// <summary>
    /// Inits dropdowns
    /// </summary>
    private void InitDropdowns()
    {
        m_DropdownUnitsData.Clear();
        m_DropdownUnits.ClearOptions();

        m_DropdownUnitsData = UnitsDataStorage.Instance.GetData();
        m_DropdownUnitsData.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));

        for (int i = 0; i < m_DropdownUnitsData.Count; i++)
        {
            m_DropdownUnits.options.Add(new Dropdown.OptionData(m_DropdownUnitsData[i].Name));
        }
        
        m_DropdownUnits.RefreshShownValue();

        if (m_ActiveTargetUnit == null)
        {
            var unitData = m_DropdownUnitsData[UnityEngine.Random.Range(0, m_DropdownUnitsData.Count)];
            var prefab = unitData.GetPrefab();
            
            if (prefab == null)
                return;
            
            m_ActiveTargetUnit = Instantiate(prefab);
            Transform model = m_ActiveTargetUnit.transform.Find("PerspModifier/Scaler/Model");
            Destroy(m_ActiveTargetUnit.GetComponent<RVOController>());
//            Destroy(m_ActiveTargetUnit.GetComponent<Unit>());
            model.gameObject.GetComponent<UnitAnimator>().Setup(null);
            m_ActiveTargetUnit.GetComponent<Unit>().Init();
            m_ActiveTargetUnit.GetComponent<Unit>().enabled = false;
            Destroy(m_ActiveTargetUnit.GetComponent<StateMachineRunner>());
            m_ActiveTargetUnit.AddComponent<UnitTest>();
            m_ActiveTargetUnit.SetActive(false);
        
            var animator = model.gameObject.GetComponent<Animator>();
            animator.SetBool(UnitAnimatorParameters.attack, false);
            animator.SetBool(UnitAnimatorParameters.walk, false);
            animator.SetBool(UnitAnimatorParameters.stunned, false);
        }
        
        OnUnitSelected(0);
    }
    
    /// <summary>
    /// Handles click units dropdown
    /// </summary>
    public void OnUnitSelected(int index)
    {
        if (m_ActiveUnit != null)
        {   
            Destroy(m_ActiveUnit);
            m_ActiveTargetUnit.SetActive(false);
        }

        m_IsUnitDead = false;
        HideHitText();
        
        var unitData = m_DropdownUnitsData[m_DropdownUnits.value];
        var unitPrefab = unitData.GetPrefab();

        if (unitPrefab == null)
        {
            Debug.LogError("Null prefab for unit. name = " + unitData.Name + ", id = " + unitData.Id);
            return;
        }

        m_AttackBoxWidth = unitData.MeleeAttackBoxWidth;
        m_AttackBoxHeight = unitData.MeleeAttackBoxHeight;
        m_ColliderRadius = unitData.DefaultRadius;
        
        m_ActiveUnit = Instantiate(unitPrefab);
        m_ActiveUnit.transform.position = new Vector3(0, 0, -3);
        var model = m_ActiveUnit.transform.Find("PerspModifier/Scaler/Model");
        model.gameObject.AddComponent<EventHandlerTest>();
        
        Destroy(m_ActiveUnit.GetComponent<StateMachineRunner>());
        Destroy(m_ActiveUnit.GetComponent<RVOController>());
        Destroy(m_ActiveUnit.GetComponent<CapsuleCollider>());
        var stepSpeedParam = m_ActiveUnit.GetComponent<Unit>().m_StepSpeed;
//        Destroy(m_ActiveUnit.GetComponent<Unit>());
        m_ActiveUnit.GetComponent<Unit>().Init();
        m_ActiveUnit.GetComponent<Unit>().enabled = false;
        m_AudioSource = m_ActiveUnit.GetComponent<AudioSource>();
        var unitTest = m_ActiveUnit.AddComponent<UnitTest>();

        unitTest.m_StepSpeed = stepSpeedParam;
        unitTest.m_MaxSpeed = unitData.MoveSpeed;

        m_ActiveUnitAnimator = m_ActiveUnit.GetComponent<Unit>().m_Animator;
        m_ActiveUnitAnimator.Setup(null);
        m_ActiveUnitAnimator.SetBool(UnitAnimatorParameters.melee, unitData.IsMelee());
        m_ActiveUnitAnimator.SetFloat(UnitAnimatorParameters.attack_speed, 1f);
        m_ActiveUnitAnimator.SetFloat(UnitAnimatorParameters.move_speed, 1f);

        var skeletonAnimator = model.gameObject.GetComponent<SkeletonAnimator>();
        m_HasVectorBone = skeletonAnimator.Skeleton.FindBone("vector") != null;
        m_HasVectorBoneLabel.gameObject.SetActive(m_HasVectorBone);

        if (m_HasVectorBone)
        {
            unitTest.m_TargetUnit = m_ActiveTargetUnit.GetComponent<UnitTest>();
            m_ActiveTargetUnit.transform.position = new Vector3(5, 0, 0);
        }
        
        InitDropdownAnimations();
        m_DropdownAnimations.value = 0;
        OnAnimationSelected(0);

        m_AttackSpeedSlider.value = 1f;
        m_ActiveUnitAnimator.SetFloat(UnitAnimatorParameters.attack_speed, 1f);

        m_UnitScaleSlider.value = unitData.HeightFactor;
    }
    
    /// <summary>
    /// Handles click animations dropdown
    /// </summary>
    public void OnAnimationSelected(int index)
    {
        if (m_ActiveUnit == null || m_ActiveUnitAnimator == null)
            return;

        if (m_IsUnitDead)
        {
            OnUnitSelected(0);
        }
        
        HideHitText();

        string animationName = m_DropdownAnimations.options[m_DropdownAnimations.value].text;
        var unitData = m_DropdownUnitsData[m_DropdownUnits.value];

        switch (animationName)
        {
            case "Summon":
                m_ActiveUnitAnimator.SetBool(UnitAnimatorParameters.walk, false);
                m_ActiveUnitAnimator.SetBool(UnitAnimatorParameters.attack, false);
                m_ActiveUnitAnimator.SetBool(UnitAnimatorParameters.stunned, false);
                m_ActiveTargetUnit.SetActive(false);
                m_ActiveUnitAnimator.SetBool(UnitAnimatorParameters.cast_ultimate, false);
                CancelCastAbility();
                
                m_ActiveUnitAnimator.PlaySummon();
                break;
            case "Idle":
                m_ActiveUnitAnimator.SetBool(UnitAnimatorParameters.walk, false);
                m_ActiveUnitAnimator.SetBool(UnitAnimatorParameters.attack, false);
                m_ActiveUnitAnimator.SetBool(UnitAnimatorParameters.stunned, false);
                m_ActiveTargetUnit.SetActive(false);
                m_ActiveUnitAnimator.SetBool(UnitAnimatorParameters.cast_ultimate, false);
                CancelCastAbility();
                break;
                
            case "Stun":
                m_ActiveUnitAnimator.SetBool(UnitAnimatorParameters.walk, false);
                m_ActiveUnitAnimator.SetBool(UnitAnimatorParameters.attack, false);
                m_ActiveUnitAnimator.SetBool(UnitAnimatorParameters.stunned, true);
                m_ActiveTargetUnit.SetActive(false);
                m_ActiveUnitAnimator.SetBool(UnitAnimatorParameters.cast_ultimate, false);
                CancelCastAbility();
                break;
                
            case "Walk":
                m_ActiveUnitAnimator.SetBool(UnitAnimatorParameters.walk, true);
                m_ActiveUnitAnimator.SetBool(UnitAnimatorParameters.attack, false);
                m_ActiveUnitAnimator.SetBool(UnitAnimatorParameters.stunned, false);
                m_ActiveTargetUnit.SetActive(false);
                m_ActiveUnitAnimator.SetBool(UnitAnimatorParameters.cast_ultimate, false);
                CancelCastAbility();
                break;
            case "Attack":
                m_ActiveUnitAnimator.SetBool(UnitAnimatorParameters.walk, false);
                m_ActiveUnitAnimator.SetBool(UnitAnimatorParameters.attack, true);
                m_ActiveUnitAnimator.SetBool(UnitAnimatorParameters.ranged_melee, false);
                m_ActiveUnitAnimator.SetBool(UnitAnimatorParameters.stunned, false);
                bool range = m_ActiveUnit.GetComponent<Unit>().m_CanUseProjectiles;
                m_ActiveTargetUnit.SetActive(range);
                m_ActiveUnitAnimator.SetBool(UnitAnimatorParameters.cast_ultimate, false);
                CancelCastAbility();
                break;
            case "Attack_Melee":
                m_ActiveUnitAnimator.SetBool(UnitAnimatorParameters.walk, false);
                m_ActiveUnitAnimator.SetBool(UnitAnimatorParameters.attack, true);
                m_ActiveUnitAnimator.SetBool(UnitAnimatorParameters.ranged_melee, true);
                m_ActiveUnitAnimator.SetBool(UnitAnimatorParameters.stunned, false);
                m_ActiveTargetUnit.SetActive(false);
                m_ActiveUnitAnimator.SetBool(UnitAnimatorParameters.cast_ultimate, false);
                CancelCastAbility();
                break;
            case "Ultimate":
                m_ActiveUnitAnimator.SetBool(UnitAnimatorParameters.walk, false);
                m_ActiveUnitAnimator.SetBool(UnitAnimatorParameters.attack, false);
                m_ActiveUnitAnimator.SetBool(UnitAnimatorParameters.cast_ultimate, true);
                m_ActiveUnitAnimator.SetBool(UnitAnimatorParameters.stunned, false);
                m_ActiveTargetUnit.SetActive(m_HasVectorBone);
                CancelCastAbility();
                break;
            case "Death":
                m_IsUnitDead = true;
                m_ActiveUnitAnimator.SetBool(UnitAnimatorParameters.walk, false);
                m_ActiveUnitAnimator.SetBool(UnitAnimatorParameters.attack, false);
                m_ActiveUnitAnimator.SetTrigger(UnitAnimatorParameters.dead);
                m_ActiveUnitAnimator.SetBool(UnitAnimatorParameters.stunned, false);
                m_ActiveTargetUnit.SetActive(false);
                m_ActiveUnitAnimator.SetBool(UnitAnimatorParameters.cast_ultimate, false);
                CancelCastAbility();
                m_AudioSource.PlayOneShot(unitData.GetDeathSound());
                break;
            case "Win":
                m_ActiveUnitAnimator.SetBool(UnitAnimatorParameters.walk, false);
                m_ActiveUnitAnimator.SetBool(UnitAnimatorParameters.attack, false);
                m_ActiveUnitAnimator.SetTrigger(UnitAnimatorParameters.win);
                m_ActiveUnitAnimator.SetBool(UnitAnimatorParameters.stunned, false);
                m_ActiveTargetUnit.SetActive(false);
                m_ActiveUnitAnimator.SetBool(UnitAnimatorParameters.cast_ultimate, false);
                CancelCastAbility();
                m_AudioSource.PlayOneShot(unitData.GetVictorySound());
                break;
            case "Cast_0":
            case "Cast_1":
            case "Cast_2":
            case "Cast_3":
            case "Cast_4":
                m_ActiveUnitAnimator.SetBool(UnitAnimatorParameters.walk, false);
                m_ActiveUnitAnimator.SetBool(UnitAnimatorParameters.attack, false);
                m_ActiveUnitAnimator.SetBool(UnitAnimatorParameters.stunned, false);
                m_ActiveTargetUnit.SetActive(m_HasVectorBone);
                m_ActiveUnitAnimator.SetBool(UnitAnimatorParameters.cast_ultimate, false);

                CancelCastAbility();
                
                if (animationName == "Cast_0")
                {
                    m_ActiveUnitAnimator.SetBool(UnitAnimatorParameters.cast_0, true);
                }
                else if (animationName == "Cast_1")
                {
                    m_ActiveUnitAnimator.SetBool(UnitAnimatorParameters.cast_1, true);
                }
                else if (animationName == "Cast_2")
                {
                    m_ActiveUnitAnimator.SetBool(UnitAnimatorParameters.cast_2, true);
                }
                else if (animationName == "Cast_3")
                {
                    m_ActiveUnitAnimator.SetBool(UnitAnimatorParameters.cast_3, true);
                }
                else if (animationName == "Cast_4")
                {
                    m_ActiveUnitAnimator.SetBool(UnitAnimatorParameters.cast_4, true);
                }
                break;
        }
    }

    ///////////////
    private void CancelCastAbility()
    {
        m_ActiveUnitAnimator.SetBool(UnitAnimatorParameters.cast_0, false);
        m_ActiveUnitAnimator.SetBool(UnitAnimatorParameters.cast_1, false);
        m_ActiveUnitAnimator.SetBool(UnitAnimatorParameters.cast_2, false);
        m_ActiveUnitAnimator.SetBool(UnitAnimatorParameters.cast_3, false);
        m_ActiveUnitAnimator.SetBool(UnitAnimatorParameters.cast_4, false);
    }
    
    /// <summary>
    /// Inits dropdown animations
    /// </summary>
    private void InitDropdownAnimations()
    {
        var unitData = m_DropdownUnitsData[m_DropdownUnits.value];
        
        m_DropdownAnimations.ClearOptions();
        m_DropdownAnimations.options.Add(new Dropdown.OptionData("Idle"));
        m_DropdownAnimations.options.Add(new Dropdown.OptionData("Stun"));

        if (!unitData.IsStationary())
        {
            m_DropdownAnimations.options.Add(new Dropdown.OptionData("Walk"));
        }
        
        if (unitData.UnitAttackType != UnitAttackType.None)
        {
            m_DropdownAnimations.options.Add(new Dropdown.OptionData("Attack"));
            
            if (!unitData.IsMelee())
            {
                m_DropdownAnimations.options.Add(new Dropdown.OptionData("Attack_Melee"));
            }
        }

        var skeletonAnimator = m_ActiveUnit.transform.Find("PerspModifier/Scaler/Model").gameObject.GetComponent<SkeletonAnimator>();
        // FIXME: !!! переделать ульту в абилку
        var attackAnim = skeletonAnimator.Skeleton.Data.Animations.Find(spineAnimation =>
            spineAnimation.Name == "cast_ultimate" || spineAnimation.Name == "cast_ultimate");
        
        
        if (attackAnim != null)
        {
            m_DropdownAnimations.options.Add(new Dropdown.OptionData("Ultimate"));
        }
        
        for (int i = 0; i < 5; i++)
        {
            string animCastName = "cast_" + i;
            var anim = skeletonAnimator.Skeleton.Data.Animations.Find(spineAnimation =>
                spineAnimation.Name == animCastName);
            
            if (anim == null)
                break;
            
            m_DropdownAnimations.options.Add(new Dropdown.OptionData("Cast_" + i));
        }
        
        m_DropdownAnimations.options.Add(new Dropdown.OptionData("Win"));
        m_DropdownAnimations.options.Add(new Dropdown.OptionData("Death"));

        if (skeletonAnimator.Skeleton.Data.Animations.Find(spineAnimation =>
                spineAnimation.Name == "summon") != null)
        {
            m_DropdownAnimations.options.Add(new Dropdown.OptionData("Summon"));
        }
        
        m_DropdownAnimations.RefreshShownValue();
    }
    
    /// <summary>
    /// Отображает название ивента нанесения урона в анимации атаки
    /// </summary>
    private void OnAttackHitEvent(string eventName)
    {
        if (eventName == "death_dust")
        {
            var fx = (GameObject)Resources.Load("Prefabs/Fx/fx_death_dust");

            if (fx != null)
            {
                var fxObj = Instantiate(fx);
                fxObj.transform.position = m_ActiveUnit.GetComponent<UnitTest>().GetBoneWorldPosition("hit_effect");
                fxObj.SetActive(true);
            }
            return;
        }
        
        HideHitText();

        if (eventName.ToLower() == "hit 2")
        {
            m_HitLabel.color = m_PrevHit2Color == Color.green ? Color.red : Color.green;
            m_PrevHit2Color = m_HitLabel.color;
        }
        else
        {
            m_HitLabel.color = Color.white;
        }
        
        m_HitLabel.text = eventName;
        m_HitLabel.gameObject.SetActive(true);
        m_HideHitTextAnimation = StartCoroutine(DoHideHitText());

        var unitData = m_DropdownUnitsData[m_DropdownUnits.value];

        if (unitData.IsMelee())
            m_AudioSource.clip = unitData.GetAttackAudioClip(AttackSoundSource.Melee);
        else if(!m_ActiveUnitAnimator.GetBool(UnitAnimatorParameters.ranged_melee))
            m_AudioSource.clip = unitData.GetAttackAudioClip(AttackSoundSource.Range);
        else
            m_AudioSource.clip = unitData.GetAttackAudioClip(AttackSoundSource.Melee);
        
        m_AudioSource.PlayOneShot(m_AudioSource.clip);

        if (!m_ActiveUnit.GetComponent<Unit>().m_CanUseProjectiles || m_ActiveUnitAnimator.GetBool(UnitAnimatorParameters.melee))
            return;
        
        if (!m_ActiveUnitAnimator.GetBool(UnitAnimatorParameters.attack))
            return;

        ProjectileData projectileData = null;

        if (!m_ActiveUnitAnimator.GetBool(UnitAnimatorParameters.ranged_melee))
        {
            projectileData = unitData.GetProjectile();
        }

        if (projectileData == null)
            return;
        
        var prefab = projectileData.GetPrefab();
        var obj = Instantiate(prefab);
        obj.transform.rotation = m_ActiveUnit.GetComponent<Unit>().m_VfxTransform.rotation;

        Destroy(obj.GetComponent<Projectile>());
        var projectileTest = obj.gameObject.AddComponent<ProjectileTest>();
        projectileTest.m_DestroyDelay = prefab.m_DestroyDelay;

        bool moveStraight = true;
        var from = m_ActiveUnit.GetComponent<Unit>();
        var to = m_ActiveTargetUnit.GetComponent<Unit>();
        var vectorBone = from.GetBone("vector");

        // если есть кость vector, то определяем направление по ней
        if (vectorBone != null && !projectileData.IsStraight)
        {
            float vectorAngle = Unit.CalculateVectorBoneAngle(Camera.main, from, to);
            moveStraight = vectorAngle <= Unit.MaxStraightRangeWeaponAngle &&
                            vectorAngle >= Unit.MinStraightRangeWeaponAngle;
        }

        projectileTest.Setup(from, to, projectileData.Speed, moveStraight);
    }

    /// <summary>
    /// Скрывает текст "Hit" (о моменте хита в анимации атаки) с задержкой по времени 
    /// </summary>
    /// <returns></returns>
    private IEnumerator DoHideHitText()
    {
        yield return new WaitForSeconds(0.25f);
        
        m_HitLabel.gameObject.SetActive(false);
        m_HideHitTextAnimation = null;
    }

    
    /// <summary>
    /// Принудительно скрывает текст "Hit" (о моменте хита в анимации атаки)
    /// </summary>
    private void HideHitText()
    {
        if (m_HideHitTextAnimation != null)
        {
            StopCoroutine(m_HideHitTextAnimation);
        }
        
        m_HideHitTextAnimation = null;
        m_HitLabel.gameObject.SetActive(false);
    }

    /// <summary>
    /// Включает/выключает режим проверки перемещения юнита
    /// </summary>
    public void OnClickTestMove()
    {
        m_AnimationViewerGroup.SetActive(!m_AnimationViewerGroup.activeSelf);
        var unit = m_ActiveUnit.GetComponent<UnitTest>();
        
        bool enable = !m_AnimationViewerGroup.activeSelf;
        unit.EnableMovement(enable);
        m_ActiveTargetUnit.SetActive(false);
        m_TestMoveBtn.GetComponentInChildren<Text>().text = !enable ? "Test Movement" : "Test Animations";
        
        m_DropdownAnimations.value = 0;
        OnAnimationSelected(0);
    }

    ///////////////
    public void OnClickFlip()
    {
        var skeleton = m_ActiveUnit.GetComponent<Unit>().m_SkeletonAnimator.Skeleton;
        skeleton.FlipX = !skeleton.FlipX;
        m_ActiveUnitAnimator.SetFlipX(skeleton.FlipX);
    }

    ///////////////
    public void OnClickTestFx()
    {
        if (m_FxTestPrefab == null)
        {
            Debug.LogWarning("you should assign fx in inspector");
            return;
        }

        Transform vfxTransform = m_ActiveUnit.GetComponent<Unit>().m_VfxTransform;
        GameObject fxObj = Instantiate(m_FxTestPrefab, vfxTransform, true);
        fxObj.transform.localRotation = Quaternion.identity;

        if (!string.IsNullOrEmpty(m_FxAnimationName))
        {
            var fxAnimator = fxObj.GetComponent<SingleAnimatorFx>();
            fxAnimator.m_AnimationName = m_FxAnimationName;
        }
        
        Vector3 pos = m_ActiveUnit.GetComponent<UnitTest>().GetBoneWorldPosition(m_TestSpineBoneFx.ToString());
        pos = vfxTransform.InverseTransformPoint(pos);
        pos.z = -0.1f;
        fxObj.transform.localPosition = pos; 
    }

    ///////////////
    public void OnClickClearTestFx()
    {
        // TODO: можно бы тут и "по честному" удалять через Stop
        Transform tr = m_ActiveUnit.GetComponent<Unit>().m_VfxTransform;
        tr.DestroyChildren();
    }

    ///////////////
    public void OnChangeAttackSpeed()
    {
        m_AttackSpeedText.text = "AS: " + m_AttackSpeedSlider.value.ToString("F2");

        if (m_ActiveUnitAnimator != null)
        {
            m_ActiveUnitAnimator.SetFloat(UnitAnimatorParameters.attack_speed, m_AttackSpeedSlider.value);
        }
    }

    ///////////////
    public void OnUnitScaleChange()
    {
        m_UnitScaleText.text = "Scale: " + m_UnitScaleSlider.value.ToString("F2");

        if (m_ActiveUnitAnimator != null)
        {
            float factor = m_UnitScaleSlider.value;
            // kostyl, так как Unit не работает полноценно в AnimationViewer
            var skeleton = m_ActiveUnit.GetComponent<Unit>().m_SkeletonAnimator.Skeleton;
            Vector3 scale = new Vector3(factor * (skeleton.flipX ? -1 : 1), factor, factor);
            m_ActiveUnitAnimator.SetScale(scale);
        }
    }

    ///////////////
    public void RefreshDropDownsByFilter()
    {
        List<UnitData> newList = OnRaceSelect();

        m_DropdownUnits.ClearOptions();

        m_DropdownUnitsData.Clear();

        if (m_SoldiersToggle.isOn)
        {
            foreach (UnitData unit in newList)
            {
                if (unit.DefaultUnitKind == UnitKind.Soldier)
                    m_DropdownUnitsData.Add(unit);
            }
        }

        if (m_HeroesToggle.isOn)
        {
            foreach (UnitData unit in newList)
            {
                if (unit.DefaultUnitKind == UnitKind.Hero)
                    m_DropdownUnitsData.Add(unit);
            }
        }

        if (m_CommonEnemiesToggle.isOn)
        {
            foreach (UnitData unit in newList)
            {
                if (unit.DefaultUnitKind == UnitKind.CommonEnemy)
                    m_DropdownUnitsData.Add(unit);
            }
        }

        if (m_BossesToggle.isOn)
        {
            foreach (UnitData unit in newList)
            {
                if (unit.DefaultUnitKind == UnitKind.BossEnemy)
                    m_DropdownUnitsData.Add(unit);
            }
        }

        if (m_BigBossesToggle.isOn)
        {
            foreach (UnitData unit in newList)
            {
                if (unit.DefaultUnitKind == UnitKind.BigBoss)
                    m_DropdownUnitsData.Add(unit);
            }
        }

        m_DropdownUnitsData.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));

        for (int i = 0; i < m_DropdownUnitsData.Count; i++)
        {
            m_DropdownUnits.options.Add(new Dropdown.OptionData(m_DropdownUnitsData[i].Name));
        }

        m_DropdownUnits.RefreshShownValue();

        if (m_DropdownUnits.options.Count > 0)
        {
            m_DropdownUnits.value = 0;
            OnUnitSelected(0);
        }
    }

    ////////////////
    public void InitRaceFilter()
    {
        m_RaceFilter.ClearOptions();

        string[] races = Enum.GetNames(typeof(UnitRace));
        
        foreach (string name in races)
        {
            m_RaceFilter.options.Add(new Dropdown.OptionData(name));
        }
        m_RaceFilter.RefreshShownValue();
    }

    ////////////////
    public List<UnitData> OnRaceSelect()
    {
        if (m_RaceFilter.value == (int)UnitRace.Unknown)
            return UnitsDataStorage.Instance.GetData();
        else
        {
            List<UnitData> newList = new List<UnitData>();

            foreach (UnitData unit in UnitsDataStorage.Instance.GetData())
            {
                if (m_RaceFilter.value == (int)unit.Race)
                    newList.Add(unit);
            }

            return newList;
        }
    }

    ///////////////
    private void OnDrawGizmos()
    {
        if (m_ActiveUnit == null)
            return;

        // отрисовываем радиус коллайдера юнита
        float radius = UnitData.CalculateUnitRadius(m_ColliderRadius);
        DrawExtension.DrawCircle(m_ActiveUnit.transform.position, Color.green, radius);
        // отрисовываем attack_box
        var bounds = new Bounds(m_ActiveUnit.transform.position, new Vector3(m_AttackBoxWidth * 2, 0, m_AttackBoxHeight));
        DrawExtension.DrawBounds(bounds, Color.red);
    }
}
