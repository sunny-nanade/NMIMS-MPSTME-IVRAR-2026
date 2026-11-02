using UnityEngine;
namespace CampusQuest {
public class GameTimer:MonoBehaviour {
 [Range(60,1800)]public float limitSeconds=1800;
 public float Elapsed {get;private set;}
 public float Remaining=>Mathf.Max(0,Mathf.Min(1800,limitSeconds)-Elapsed);
 public string RemainingText=>System.TimeSpan.FromSeconds(Remaining).ToString(@"mm\:ss");
 public void Tick(float dt){Elapsed+=Mathf.Max(0,dt);}
 public void ResetSession()=>Elapsed=0;
}
}
