using UnityEngine;
namespace CampusQuest {
public class Coupon : MonoBehaviour {
 public GameObject visual;
 public TextMesh label;
 void Update(){var m=CanteenOrderManager.Instance;bool show=m!=null&&m.Step==OrderStep.CouponReady;if(visual!=null&&visual.activeSelf!=show)visual.SetActive(show);if(show&&label!=null)label.text=m.Ticket+"\nE — collect";}
 public void Take(){if(CanteenOrderManager.Instance!=null)CanteenOrderManager.Instance.TakeCoupon();}
}
}
