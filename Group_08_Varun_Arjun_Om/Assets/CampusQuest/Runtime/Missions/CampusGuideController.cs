using UnityEngine;
namespace CampusQuest {
public class CampusGuideController:MonoBehaviour {
 [TextArea]public string Subtitle;
 public AudioSource voice;
 public void Say(string text,AudioClip clip=null){Subtitle=text;if(voice!=null){voice.Stop();if(clip!=null){voice.clip=clip;voice.Play();}}}
}
}
