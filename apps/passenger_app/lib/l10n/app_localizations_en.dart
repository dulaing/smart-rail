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
}
