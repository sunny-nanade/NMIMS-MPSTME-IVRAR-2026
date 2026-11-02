using System;
using UnityEngine;
namespace CampusQuest {
public class CanteenOrderManager : MonoBehaviour {
 public static CanteenOrderManager Instance {get;private set;}
 public MenuItemData[] menuItems=new MenuItemData[0];
 public int completionReward=100;
 public Material propMaterial;
 public readonly CanteenOrderState Order=new CanteenOrderState();
 public MenuItemData SelectedItem {get;private set;}
 public string Feedback {get;private set;}="Choose a dish at the ordering counter.";
 public event Action Completed;
 GameObject held;
 public OrderStep Step=>Order.Step;
 public bool CanInteract=>SceneTransitionManager.Instance!=null&&!SceneTransitionManager.Instance.Busy&&SceneTransitionManager.Instance.CurrentLevel==-1;
 public string Ticket=>SelectedItem==null?"":"COUPON #"+Order.OrderNumber.ToString("000")+"\n"+SelectedItem.displayName+" | INR "+SelectedItem.priceINR;
 public string Objective {
  get {switch(Step){
   case OrderStep.CouponReady:return "Collect your coupon from the ordering counter (E).";
   case OrderStep.CouponHeld:return "Carry your coupon to the serving counter. Press E to hand it over.";
   case OrderStep.TrayReady:return "Collect your tray from the serving counter (E).";
   case OrderStep.Complete:return "Canteen order complete! You can carry the tray to a seat.";
   default:return "Find the ordering counter. Press E on the menu board and choose a dish.";
  }}
 }
 void Awake(){if(Instance!=null&&Instance!=this){Destroy(this);return;}Instance=this;}
 public void Hint(string value){Feedback=value;}
 public void SelectDish(MenuItemData item){
  if(!CanInteract)return;
  if(item==null||item.isPlaceholder||!item.isVerified||item.priceINR<0||menuItems==null||Array.IndexOf(menuItems,item)<0){Hint("This menu item is unavailable.");return;}
  if(!Order.SelectDish()){Hint(Objective);return;}
  SelectedItem=item;Hint("Order placed. Your coupon is on the counter — collect it with E.");
 }
 public void TakeCoupon(){if(!CanInteract)return;if(!Order.TakeCoupon()){Hint(Objective);return;}RefreshHeld();Hint("Coupon collected. Go to the serving counter.");}
 public void HandCoupon(){
  if(!CanInteract)return;
  if(!Order.HandCoupon()){Hint(Step==OrderStep.ChooseDish?"Get your coupon first: choose a dish at the ordering counter.":Objective);return;}
  RefreshHeld();Hint("Coupon accepted. Your tray is ready — look at it and press E.");
 }
 public void TakeTray(){
  if(!CanInteract)return;
  bool first=!Order.RewardGranted;
  if(!Order.TakeTray(completionReward)){Hint(Objective);return;}
  RefreshHeld();Hint(first?"Order complete! +"+Mathf.Max(0,completionReward)+" points.":"Practice order complete. Your completion reward has already been earned.");
  if(first)Completed?.Invoke();
 }
 public void Replay(){Order.Replay();SelectedItem=null;RefreshHeld();Hint("Practice restarted. Choose a dish. Earned points are retained.");}
 public void ResetSession(){Order.ResetSession();SelectedItem=null;RefreshHeld();Hint("New session. Choose a dish at the ordering counter.");}
 void RefreshHeld(){
  if(held!=null){held.SetActive(false);Destroy(held);held=null;}
  if(Step!=OrderStep.CouponHeld&&Step!=OrderStep.Complete)return;
  var player=SceneTransitionManager.Instance==null?null:SceneTransitionManager.Instance.player;
  if(player==null||player.view==null)return;
  held=new GameObject("Held canteen item — desktop carry");held.transform.SetParent(player.view.transform,false);
  held.transform.localPosition=new Vector3(.22f,-.26f,.65f);
  var body=GameObject.CreatePrimitive(PrimitiveType.Cube);body.transform.SetParent(held.transform,false);body.layer=2;
  body.transform.localScale=Step==OrderStep.CouponHeld?new Vector3(.27f,.18f,.012f):new Vector3(.42f,.025f,.27f);
  body.GetComponent<Collider>().enabled=false;Destroy(body.GetComponent<Collider>());
  if(propMaterial!=null)body.GetComponent<Renderer>().sharedMaterial=propMaterial;
  var label=new GameObject("Held item label");label.layer=2;label.transform.SetParent(held.transform,false);label.transform.localPosition=new Vector3(0,.025f,-.02f);
  var text=label.AddComponent<TextMesh>();text.text=Step==OrderStep.CouponHeld?Ticket:"TRAY\n"+(SelectedItem==null?"":SelectedItem.displayName);text.characterSize=.012f;text.fontSize=48;text.anchor=TextAnchor.MiddleCenter;text.alignment=TextAlignment.Center;text.color=Color.black;
  text.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");label.GetComponent<MeshRenderer>().sharedMaterial=text.font.material;
 }
 void OnDestroy(){if(Instance==this)Instance=null;if(held!=null)Destroy(held);}
}
}
