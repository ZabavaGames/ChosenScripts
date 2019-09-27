using System;
using System.Collections;
using UnityEngine;

// названия менять вместе с именами клипов в клиенте
public enum ButtonSoundType
{
    SimpleButton = 0,
    CrystalButton = 1,
}

public class SoundController : MonoBehaviour
{
    public static SoundController Instance { get; private set; }

    public AudioSource m_AudioSource;
    
    //////////////
    private void Awake()
    {
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    //////////////
    public void PlaySound(string path)
    {
        if (!LocalGameState.IsSoundsEnabled)
            return;

        float volumeScale = 1f;

        // проверяем, надо ли понизить громкость для звука
        if (NeedSetVolumeScaleForSound(path))
        {
            volumeScale = 0.5f;
        }

        m_AudioSource.PlayOneShot(Resources.Load<AudioClip>(path), volumeScale);
    }

    //////////////
    public static void PlaySoundExternal(AudioSource source, string path, bool stopPrevious)
    {
        if (!LocalGameState.IsSoundsEnabled)
            return;

        if (stopPrevious)
            source.Stop();

        source.PlayOneShot(Resources.Load<AudioClip>(path), 1f);
    }

    //////////////
    private bool NeedSetVolumeScaleForSound(string path)
    {
        return path.Equals(SoundName.VictoryDialogSound);
    }

    //////////////
    public void PlayButtonSound(ButtonSoundType type)
    {
        if (!LocalGameState.IsSoundsEnabled)
            return;

        switch (type)
        {
            case ButtonSoundType.SimpleButton:
                PlaySound(SoundName.SimpleButtonSound);
                break;

            case ButtonSoundType.CrystalButton:
                PlaySound(SoundName.CrystalButtonSound);
                break;
        }
    }

    //////////////
    public float GetSoundLength(string path)
    {
        return Resources.Load<AudioClip>(path).length;
    }
}
