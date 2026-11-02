using UnityEngine;
namespace CampusQuest {
public class LiftDoor : MonoBehaviour {
 public Transform left,right; public float travel=.65f;
 Vector3 a,b;float amount;public bool IsOpen {get;private set;}
 void Awake(){a=left.localPosition;b=right.localPosition;}
 void Update(){amount=Mathf.MoveTowards(amount,IsOpen?1:0,Time.deltaTime*2);left.localPosition=a+Vector3.left*travel*amount;right.localPosition=b+Vector3.right*travel*amount;}
 public void SetOpen(bool value){IsOpen=value;}
}
}
