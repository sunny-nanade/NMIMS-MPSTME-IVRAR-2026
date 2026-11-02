using System.Collections.Generic;
using UnityEngine;
namespace CampusQuest {
public class CheckpointManager:MonoBehaviour {
 readonly HashSet<string> signals=new HashSet<string>();
 public bool Has(string id)=>signals.Contains(id);
 public bool Mark(string id)=>!string.IsNullOrEmpty(id)&&signals.Add(id);
 public void ResetSession()=>signals.Clear();
}
}
