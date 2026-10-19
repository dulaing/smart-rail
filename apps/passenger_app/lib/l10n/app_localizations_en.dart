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
  String get guest => 'Guest';

  @override
  String get guestNotice =>
      'Timetables and train times are available without an account.';

  @override
  String get signIn => 'Sign in';

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
  String get homeNextTrains => 'Your next trains';

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
}
