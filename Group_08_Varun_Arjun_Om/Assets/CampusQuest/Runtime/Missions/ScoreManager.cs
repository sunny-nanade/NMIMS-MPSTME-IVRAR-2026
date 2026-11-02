using System.Collections.Generic;
using UnityEngine;
namespace CampusQuest {
public class ScoreManager:MonoBehaviour {
 public ScoringRules rules;
 public int Total {get;private set;}
 readonly HashSet<string> awarded=new HashSet<string>();
 public bool Award(string key,int points){if(!awarded.Add(key))return false;Total+=Mathf.Max(0,points);return true;}
 public void Penalty(int points){Total+=Mathf.Min(0,points);}
 public void ResetSession(){Total=0;awarded.Clear();}
}
}
