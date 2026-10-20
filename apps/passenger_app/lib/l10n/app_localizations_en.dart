// ignore: unused_import
import 'package:intl/intl.dart' as intl;

import 'app_localizations.dart';

// ignore_for_file: type=lint

/// The translations for English (`en`).
class AppLocalizationsEn extends AppLocalizations {
  AppLocalizationsEn([String locale = 'en']) : super(locale);

  @override
  String get scheduledArrivalOrigin => 'Scheduled arrival at origin';

  @override
  String get scheduledDeparture => 'Scheduled departure';

  @override
  String get expectedDeparture => 'Expected departure';

  @override
  String get scheduledArrival => 'Scheduled arrival';

  @override
  String get expectedArrival => 'Expected arrival';

  @override
  String get plannedPlatform => 'Planned platform';

  @override
  String get platformUnavailable => 'Platform not provided';

  @override
  String get platformConfirmed => 'Staff confirmed platform';

  @override
  String get overdueUnknown =>
      'Scheduled departure has passed; progress is unknown.';

  @override
  String get endsEarly =>
      'This train ends early. Check the final stop in train details.';

  @override
  String get serviceDate => 'Service date';

  @override
  String get timetableSource => 'Timetable source';

  @override
  String get publicRailwayTimetable => 'Public railway timetable';

  @override
  String get scheduledStops => 'Route · scheduled stops';

  @override
  String get cancelledNotice => 'is cancelled';

  @override
  String get endsAt => 'ends early at';

  @override
  String staffConfirmedChange(Object time) {
    return 'Staff-confirmed change · $time';
  }

  @override
  String get timeUnavailable => 'time unavailable';

  @override
  String get arrivalShort => 'Arr';

  @override
  String get departureShort => 'Dep';

  @override
  String savedNoticeDetails(Object time) {
    return 'Saved notice · unverified · $time';
  }

  @override
  String savedDownloadedAt(Object time) {
    return 'Downloaded $time';
  }

  @override
  String get appTitle => 'SmartRail';

  @override
  String get tabHome => 'Home';

  @override
  String get tabTrains => 'Trains';

  @override
  String get tabFood => 'Food';

  @override
  String get tabAccount => 'Account';

  @override
  String get navLabel => 'Main navigation';

  @override
  String get comingSoon => 'Coming soon';

  @override
  String get guest => 'Browsing as guest';

  @override
  String get guestNotice => 'Sign in only to follow trains or order food.';

  @override
  String get guestBrowseNote => 'Train browsing works without an account.';

  @override
  String get signIn => 'Sign in to demo';

  @override
  String get signOut => 'Sign out';

  @override
  String get signInFailed => 'Sign-in was cancelled or failed.';

  @override
  String get noAccount =>
      'You are signed in, but there is no SmartRail account for you.';

  @override
  String get backToGuest => 'Back to guest';

  @override
  String get language => 'Language';

  @override
  String get appearance => 'Appearance';

  @override
  String get theme => 'Theme';

  @override
  String get themeSystem => 'System';

  @override
  String get themeLight => 'Light';

  @override
  String get themeDark => 'Dark';

  @override
  String get preferencesNote => 'Saved on this device only.';

  @override
  String get statusSample => 'Status sample';

  @override
  String get homeNextTrains => 'Next train';

  @override
  String get homeGreeting => 'Good morning';

  @override
  String get homeBoardHere => 'BOARD HERE';

  @override
  String get loadingStations => 'Loading stations…';

  @override
  String get stationsUnavailable =>
      'Station list unavailable. Try again when connected.';

  @override
  String get savedJourneyMissing =>
      'The saved timetable does not include this train. Reconnect to load the full route.';

  @override
  String get trainHeader => 'Trains';

  @override
  String get trainDetails => 'Train details';

  @override
  String get trainIntro =>
      'Compare scheduled times with SmartRail simulation evidence. Timetable and running information are separate.';

  @override
  String get routeFrom => 'From';

  @override
  String get routeTo => 'To';

  @override
  String get savedOffline =>
      'Saved timetable · offline. Scheduled times only; no live estimates or platform overrides.';

  @override
  String get coldOffline =>
      'Offline and no saved timetable. Connect to download public train information.';

  @override
  String get liveRefreshFailed =>
      'Live train information could not refresh. Showing the last successful response.';

  @override
  String get partialSchedule =>
      'Live information loaded; the saved timetable could not refresh.';

  @override
  String get noFutureTrains =>
      'No future scheduled trains for this route in the saved timetable.';

  @override
  String get noTrains =>
      'No trains are scheduled for this route in the current service window.';

  @override
  String get confirmedChanges => 'Confirmed service changes';

  @override
  String get savedNotices => 'Saved confirmed notices · unverified';

  @override
  String get browseAllTrains => 'Browse all trains';

  @override
  String get journeyDetails => 'Journey details';

  @override
  String get routeDiagram => 'Route · scheduled stops';

  @override
  String get scheduleSource => 'Public timetable';

  @override
  String get noSavedJourney =>
      'The saved timetable does not include this train. Reconnect to load the full route.';

  @override
  String get notServed => 'Not served';

  @override
  String get simulatedRunning =>
      'Running information, when available, is a SmartRail simulation.';

  @override
  String get foodTitle => 'Food';

  @override
  String get foodIntro =>
      'Order food to collect at a station stop on your train. You pay at the kiosk when you collect.';

  @override
  String get foodChooseTrain => 'Choose a train';

  @override
  String get foodChangeTrain => 'Change train';

  @override
  String get foodNoTrainChosen =>
      'Pick a dated train to see where you can collect food.';

  @override
  String foodTrainLine(Object train, Object from, Object to) {
    return '$train · $from → $to';
  }

  @override
  String get foodToday => 'Today';

  @override
  String get foodOnRoute => 'On your Trains route';

  @override
  String get foodNoTrains =>
      'No upcoming trains are saved yet. Open Trains while connected to load the timetable.';

  @override
  String get foodPickupHeading => 'Where to collect';

  @override
  String foodArrives(Object time) {
    return 'Train due $time';
  }

  @override
  String foodScheduledAt(Object time) {
    return 'Scheduled $time';
  }

  @override
  String get foodNoEstimate => 'No arrival estimate';

  @override
  String foodOrderBy(Object time) {
    return 'Order by $time';
  }

  @override
  String get foodSeeMenu => 'See menu';

  @override
  String get foodNoPickups => 'This train has no food collection points.';

  @override
  String get foodUnavailableHeading => 'Not available to order';

  @override
  String get foodReasonJourneyNotToday =>
      'This train does not run today, so food cannot be ordered yet.';

  @override
  String get foodReasonStopNotServed => 'This train does not serve this stop.';

  @override
  String get foodReasonStopPassed => 'The train has already passed this stop.';

  @override
  String get foodReasonTrainUnknown =>
      'SmartRail has no recent position for this train, so a pickup time cannot be promised.';

  @override
  String get foodReasonGeneric => 'Food cannot be ordered here right now.';

  @override
  String get foodProviderUnavailable =>
      'Food ordering is unavailable right now. Train information is not affected.';

  @override
  String get foodLoadFailed =>
      'Could not load food information. Check your connection and try again.';

  @override
  String get foodJourneyNotFound =>
      'SmartRail no longer knows this train. Choose another one.';

  @override
  String get foodRetry => 'Try again';

  @override
  String get foodStaleOptions =>
      'Could not refresh. These options may be out of date.';

  @override
  String foodMenuHeading(Object point) {
    return 'Menu · $point';
  }

  @override
  String get foodMenuEmpty =>
      'This collection point has no items available right now.';

  @override
  String get foodSoldOut => 'Sold out';

  @override
  String foodAddItem(Object item) {
    return 'Add one $item';
  }

  @override
  String foodRemoveItem(Object item) {
    return 'Remove one $item';
  }

  @override
  String foodQuantityOf(Object item, int count) {
    return '$item: $count in basket';
  }

  @override
  String foodBasketSummary(int count, Object total) {
    String _temp0 = intl.Intl.pluralLogic(
      count,
      locale: localeName,
      other: '$count items',
      one: '1 item',
    );
    return '$_temp0 · $total';
  }

  @override
  String get foodReview => 'Review order';

  @override
  String get foodBasketAdjusted =>
      'The menu changed, so your basket was updated.';

  @override
  String get foodPickupUnavailableNow =>
      'This pickup is no longer available to order.';

  @override
  String get foodBack => 'Back to pickup points';

  @override
  String get foodReviewHeading => 'Review your order';

  @override
  String foodCollectAt(Object point) {
    return 'Collect at $point';
  }

  @override
  String foodTrainExpected(Object time) {
    return 'Train expected $time. This is a SmartRail estimate and can change.';
  }

  @override
  String get foodPayAtKiosk =>
      'Pay at the kiosk when you collect. There is no online payment.';

  @override
  String get foodPayAtPickup => 'Pay when you collect';

  @override
  String get foodNoShowPolicy =>
      'No-show: if you do not collect your order, the provider marks it No-show after the train leaves.';

  @override
  String get foodCancelPolicy =>
      'You can cancel only while the provider still accepts cancellations. SmartRail checks this when you cancel.';

  @override
  String get foodTotal => 'Total';

  @override
  String get foodSignInToOrder => 'Sign in to order';

  @override
  String get foodSignInNote =>
      'You can keep browsing without an account. Your basket stays here while you sign in.';

  @override
  String get foodSignInCancelled =>
      'Sign-in was cancelled. Your basket is still here.';

  @override
  String get foodNoPermission => 'Your account cannot place food orders.';

  @override
  String get foodPlaceOrder => 'Place order';

  @override
  String get foodBackToMenu => 'Back to menu';

  @override
  String get foodSubmitting => 'Placing your order…';

  @override
  String get foodUncertainTitle => 'Order outcome not yet confirmed';

  @override
  String get foodUncertainBody =>
      'We have not heard back from the food provider yet. Your order is saved under one reference, so checking again never places a second order. You can leave this screen and come back.';

  @override
  String get foodCheckAgain => 'Check again';

  @override
  String get foodChecking => 'Checking…';

  @override
  String get foodConflictBody =>
      'SmartRail says this request reference was already used for a different order. Nothing was changed. Check your orders before trying again.';

  @override
  String get foodDiscardRequest => 'Discard this request';

  @override
  String get foodRefusedTitle => 'Order not placed';

  @override
  String get foodRefusedBack => 'Back to basket';

  @override
  String get foodReasonCutoffPassed =>
      'The order cutoff for this pickup has passed.';

  @override
  String get foodReasonStockInsufficient =>
      'There is not enough stock for something in your basket.';

  @override
  String get foodReasonItemUnavailable =>
      'An item in your basket is no longer available.';

  @override
  String get foodReasonPickupMismatch =>
      'This pickup no longer matches the train. Choose it again.';

  @override
  String get foodReasonOrderNotFound => 'SmartRail could not find this order.';

  @override
  String get foodReasonPassengerCancelled => 'You cancelled this order.';

  @override
  String get foodReasonTrainCancelled =>
      'The train was cancelled, so this order was cancelled.';

  @override
  String get foodReasonTrainEndedEarly =>
      'The train ends before this stop, so this order was cancelled.';

  @override
  String get foodSessionEnded =>
      'Your sign-in ended. Sign in again to see your order. Nothing is shown until you do.';

  @override
  String get foodStorageFailed =>
      'Could not save your order on this phone, so it was not sent. Try again.';

  @override
  String get foodOrderConfirmed => 'Order confirmed';

  @override
  String get foodPickupCode => 'Pickup code';

  @override
  String get foodPickupCodeHint =>
      'Show this code at the kiosk. It is only shown here, to you.';

  @override
  String foodEvidenceAt(Object time) {
    return 'Estimate from train evidence at $time';
  }

  @override
  String get foodStatusPlacing => 'Placing';

  @override
  String get foodStatusRejected => 'Not placed';

  @override
  String get foodStatusAccepted => 'Accepted';

  @override
  String get foodStatusReady => 'Ready to collect';

  @override
  String get foodStatusCollected => 'Collected';

  @override
  String get foodStatusNoShow => 'No-show';

  @override
  String get foodStatusCancelled => 'Cancelled';

  @override
  String get foodYourOrder => 'Your order';

  @override
  String get foodOrderDone => 'Done';

  @override
  String get foodOpenOrder => 'See your order';

  @override
  String get foodCancelOrder => 'Cancel order';

  @override
  String get foodCancelConfirmTitle => 'Cancel this order?';

  @override
  String get foodCancelConfirmBody =>
      'SmartRail will ask the food provider. It can refuse, for example after its cutoff.';

  @override
  String get foodKeepOrder => 'Keep order';

  @override
  String get foodCancelling => 'Cancelling…';

  @override
  String get foodCancelUnavailable =>
      'The food provider could not be reached. Nothing was changed; try again.';

  @override
  String get foodCancelUnconfirmed =>
      'We could not confirm whether the cancellation went through. The order shown is what SmartRail last reported. Check again before trying a second time.';

  @override
  String get foodReasonInvalidState => 'This order can no longer be cancelled.';

  @override
  String get foodReasonPlacementPending =>
      'The order is still being placed. Try again shortly.';

  @override
  String get foodRecentOrders => 'Your orders';

  @override
  String foodOrderPlacedAt(Object time) {
    return 'Placed $time';
  }

  @override
  String get foodPendingOrderWaiting =>
      'You have an order waiting for confirmation, so a new order cannot be placed yet.';

  @override
  String get foodBackToFood => 'Back to Food';

  @override
  String get foodOrderHistory => 'Order history';
}
