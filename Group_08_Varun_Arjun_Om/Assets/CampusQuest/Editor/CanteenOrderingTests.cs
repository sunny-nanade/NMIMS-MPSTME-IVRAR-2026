#if UNITY_EDITOR
using System;
using UnityEngine;
namespace CampusQuest.Editor {
public static class CanteenOrderingTests {
 static int checks;
 static void Check(bool value,string message){checks++;if(!value)throw new InvalidOperationException("Order test failed: "+message);}
 public static void Run(){
 checks=0;var s=new CanteenOrderState();
 Check(s.Step==OrderStep.ChooseDish,"initial step");
 Check(!s.TakeCoupon()&&!s.HandCoupon()&&!s.TakeTray(100),"wrong steps before selection rejected");
 Check(s.Points==0&&s.OrderNumber==0,"wrong steps have no reward or coupon");
 Check(s.SelectDish()&&s.OrderNumber==1,"first order ticket");
 Check(!s.SelectDish()&&!s.HandCoupon()&&!s.TakeTray(100),"coupon must be collected first");
 Check(s.TakeCoupon()&&s.Step==OrderStep.CouponHeld,"collect coupon");
 Check(!s.TakeCoupon()&&!s.TakeTray(100),"duplicate collect / skipped handover rejected");
 Check(s.HandCoupon()&&s.Step==OrderStep.TrayReady,"handover");
 Check(!s.HandCoupon()&&s.Points==0,"handover does not complete mission");
 Check(s.TakeTray(100)&&s.Points==100&&s.CompletionCount==1,"reward on tray collection only");
 Check(!s.TakeTray(100)&&s.Points==100&&s.CompletionCount==1,"duplicate input cannot farm reward");
 s.Replay();Check(s.Points==100&&s.Step==OrderStep.ChooseDish,"replay preserves earned reward");
 Check(s.SelectDish()&&s.OrderNumber==2,"replay new ticket");s.TakeCoupon();s.HandCoupon();s.TakeTray(100);
 Check(s.Points==100&&s.CompletionCount==2,"practice does not repeat reward");
 s.ResetSession();Check(s.Points==0&&s.OrderNumber==0&&!s.RewardGranted&&s.CompletionCount==0,"restart resets session");
 // Reset must recover from every intermediate stage.
 for(int stage=0;stage<5;stage++){
 s.ResetSession();if(stage>0)s.SelectDish();if(stage>1)s.TakeCoupon();if(stage>2)s.HandCoupon();if(stage>3)s.TakeTray(100);
 s.Replay();Check(s.Step==OrderStep.ChooseDish&&s.SelectDish(),"replay recovery stage "+stage);
 }
 s.ResetSession();s.SelectDish();s.TakeCoupon();s.HandCoupon();s.TakeTray(-50);Check(s.Points==0,"negative reward clamped");
 Debug.Log("Canteen order state tests passed: "+checks+" checks.");
 }
}
}
#endif
