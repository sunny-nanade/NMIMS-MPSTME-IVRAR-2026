using UnityEngine;
namespace CampusQuest {
public class ServingCounter : MonoBehaviour {
 public void HandOver(){if(CanteenOrderManager.Instance!=null)CanteenOrderManager.Instance.HandCoupon();}
}
}
