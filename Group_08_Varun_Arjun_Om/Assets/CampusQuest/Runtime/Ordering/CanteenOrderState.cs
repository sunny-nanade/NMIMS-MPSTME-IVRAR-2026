namespace CampusQuest {
public enum OrderStep { ChooseDish, CouponReady, CouponHeld, TrayReady, Complete }
// No Unity dependencies: all step changes are guarded, including duplicate input.
public sealed class CanteenOrderState {
 public OrderStep Step { get; private set; } = OrderStep.ChooseDish;
 public int OrderNumber { get; private set; }
 public int Points { get; private set; }
 public int CompletionCount { get; private set; }
 public bool RewardGranted { get; private set; }
 public bool SelectDish() { if(Step!=OrderStep.ChooseDish)return false;OrderNumber++;Step=OrderStep.CouponReady;return true; }
 public bool TakeCoupon() { if(Step!=OrderStep.CouponReady)return false;Step=OrderStep.CouponHeld;return true; }
 public bool HandCoupon() { if(Step!=OrderStep.CouponHeld)return false;Step=OrderStep.TrayReady;return true; }
 public bool TakeTray(int reward) {
  if(Step!=OrderStep.TrayReady)return false;
  Step=OrderStep.Complete;CompletionCount++;
  if(!RewardGranted){Points+=System.Math.Max(0,reward);RewardGranted=true;}
  return true;
 }
 // Replay does not erase earned points or allow repeated completion rewards.
 public void Replay() { Step=OrderStep.ChooseDish; }
 public void ResetSession(){Step=OrderStep.ChooseDish;OrderNumber=0;Points=0;CompletionCount=0;RewardGranted=false;}
}
}
