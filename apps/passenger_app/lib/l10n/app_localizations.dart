import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:flutter/widgets.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:intl/intl.dart' as intl;

import 'app_localizations_en.dart';
import 'app_localizations_si.dart';
import 'app_localizations_ta.dart';

// ignore_for_file: type=lint

/// Callers can lookup localized strings with an instance of AppLocalizations
/// returned by `AppLocalizations.of(context)`.
///
/// Applications need to include `AppLocalizations.delegate()` in their app's
/// `localizationDelegates` list, and the locales they support in the app's
/// `supportedLocales` list. For example:
///
/// ```dart
/// import 'l10n/app_localizations.dart';
///
/// return MaterialApp(
///   localizationsDelegates: AppLocalizations.localizationsDelegates,
///   supportedLocales: AppLocalizations.supportedLocales,
///   home: MyApplicationHome(),
/// );
/// ```
///
/// ## Update pubspec.yaml
///
/// Please make sure to update your pubspec.yaml to include the following
/// packages:
///
/// ```yaml
/// dependencies:
///   # Internationalization support.
///   flutter_localizations:
///     sdk: flutter
///   intl: any # Use the pinned version from flutter_localizations
///
///   # Rest of dependencies
/// ```
///
/// ## iOS Applications
///
/// iOS applications define key application metadata, including supported
/// locales, in an Info.plist file that is built into the application bundle.
/// To configure the locales supported by your app, you’ll need to edit this
/// file.
///
/// First, open your project’s ios/Runner.xcworkspace Xcode workspace file.
/// Then, in the Project Navigator, open the Info.plist file under the Runner
/// project’s Runner folder.
///
/// Next, select the Information Property List item, select Add Item from the
/// Editor menu, then select Localizations from the pop-up menu.
///
/// Select and expand the newly-created Localizations item then, for each
/// locale your application supports, add a new item and select the locale
/// you wish to add from the pop-up menu in the Value field. This list should
/// be consistent with the languages listed in the AppLocalizations.supportedLocales
/// property.
abstract class AppLocalizations {
  AppLocalizations(String locale)
    : localeName = intl.Intl.canonicalizedLocale(locale.toString());

  final String localeName;

  static AppLocalizations of(BuildContext context) {
    return Localizations.of<AppLocalizations>(context, AppLocalizations)!;
  }

  static const LocalizationsDelegate<AppLocalizations> delegate =
      _AppLocalizationsDelegate();

  /// A list of this localizations delegate along with the default localizations
  /// delegates.
  ///
  /// Returns a list of localizations delegates containing this delegate along with
  /// GlobalMaterialLocalizations.delegate, GlobalCupertinoLocalizations.delegate,
  /// and GlobalWidgetsLocalizations.delegate.
  ///
  /// Additional delegates can be added by appending to this list in
  /// MaterialApp. This list does not have to be used at all if a custom list
  /// of delegates is preferred or required.
  static const List<LocalizationsDelegate<dynamic>> localizationsDelegates =
      <LocalizationsDelegate<dynamic>>[
        delegate,
        GlobalMaterialLocalizations.delegate,
        GlobalCupertinoLocalizations.delegate,
        GlobalWidgetsLocalizations.delegate,
      ];

  /// A list of this localizations delegate's supported locales.
  static const List<Locale> supportedLocales = <Locale>[
    Locale('en'),
    Locale('si'),
    Locale('ta'),
  ];

  /// No description provided for @scheduledArrivalOrigin.
  ///
  /// In en, this message translates to:
  /// **'Scheduled arrival at origin'**
  String get scheduledArrivalOrigin;

  /// No description provided for @scheduledDeparture.
  ///
  /// In en, this message translates to:
  /// **'Scheduled departure'**
  String get scheduledDeparture;

  /// No description provided for @expectedDeparture.
  ///
  /// In en, this message translates to:
  /// **'Expected departure'**
  String get expectedDeparture;

  /// No description provided for @scheduledArrival.
  ///
  /// In en, this message translates to:
  /// **'Scheduled arrival'**
  String get scheduledArrival;

  /// No description provided for @expectedArrival.
  ///
  /// In en, this message translates to:
  /// **'Expected arrival'**
  String get expectedArrival;

  /// No description provided for @plannedPlatform.
  ///
  /// In en, this message translates to:
  /// **'Planned platform'**
  String get plannedPlatform;

  /// No description provided for @platformUnavailable.
  ///
  /// In en, this message translates to:
  /// **'Platform not provided'**
  String get platformUnavailable;

  /// No description provided for @platformConfirmed.
  ///
  /// In en, this message translates to:
  /// **'Staff confirmed platform'**
  String get platformConfirmed;

  /// No description provided for @overdueUnknown.
  ///
  /// In en, this message translates to:
  /// **'Scheduled departure has passed; progress is unknown.'**
  String get overdueUnknown;

  /// No description provided for @endsEarly.
  ///
  /// In en, this message translates to:
  /// **'This train ends early. Check the final stop in train details.'**
  String get endsEarly;

  /// No description provided for @serviceDate.
  ///
  /// In en, this message translates to:
  /// **'Service date'**
  String get serviceDate;

  /// No description provided for @timetableSource.
  ///
  /// In en, this message translates to:
  /// **'Timetable source'**
  String get timetableSource;

  /// No description provided for @publicRailwayTimetable.
  ///
  /// In en, this message translates to:
  /// **'Public railway timetable'**
  String get publicRailwayTimetable;

  /// No description provided for @scheduledStops.
  ///
  /// In en, this message translates to:
  /// **'Route · scheduled stops'**
  String get scheduledStops;

  /// No description provided for @cancelledNotice.
  ///
  /// In en, this message translates to:
  /// **'is cancelled'**
  String get cancelledNotice;

  /// No description provided for @endsAt.
  ///
  /// In en, this message translates to:
  /// **'ends early at'**
  String get endsAt;

  /// No description provided for @staffConfirmedChange.
  ///
  /// In en, this message translates to:
  /// **'Staff-confirmed change · {time}'**
  String staffConfirmedChange(Object time);

  /// No description provided for @timeUnavailable.
  ///
  /// In en, this message translates to:
  /// **'time unavailable'**
  String get timeUnavailable;

  /// No description provided for @arrivalShort.
  ///
  /// In en, this message translates to:
  /// **'Arr'**
  String get arrivalShort;

  /// No description provided for @departureShort.
  ///
  /// In en, this message translates to:
  /// **'Dep'**
  String get departureShort;

  /// No description provided for @savedNoticeDetails.
  ///
  /// In en, this message translates to:
  /// **'Saved notice · unverified · {time}'**
  String savedNoticeDetails(Object time);

  /// No description provided for @savedDownloadedAt.
  ///
  /// In en, this message translates to:
  /// **'Downloaded {time}'**
  String savedDownloadedAt(Object time);

  /// No description provided for @appTitle.
  ///
  /// In en, this message translates to:
  /// **'SmartRail'**
  String get appTitle;

  /// No description provided for @tabHome.
  ///
  /// In en, this message translates to:
  /// **'Home'**
  String get tabHome;

  /// No description provided for @tabTrains.
  ///
  /// In en, this message translates to:
  /// **'Trains'**
  String get tabTrains;

  /// No description provided for @tabFood.
  ///
  /// In en, this message translates to:
  /// **'Food'**
  String get tabFood;

  /// No description provided for @tabAccount.
  ///
  /// In en, this message translates to:
  /// **'Account'**
  String get tabAccount;

  /// No description provided for @navLabel.
  ///
  /// In en, this message translates to:
  /// **'Main navigation'**
  String get navLabel;

  /// No description provided for @comingSoon.
  ///
  /// In en, this message translates to:
  /// **'Coming soon'**
  String get comingSoon;

  /// No description provided for @guest.
  ///
  /// In en, this message translates to:
  /// **'Guest'**
  String get guest;

  /// No description provided for @guestNotice.
  ///
  /// In en, this message translates to:
  /// **'Timetables and train times are available without an account.'**
  String get guestNotice;

  /// No description provided for @signIn.
  ///
  /// In en, this message translates to:
  /// **'Sign in'**
  String get signIn;

  /// No description provided for @signOut.
  ///
  /// In en, this message translates to:
  /// **'Sign out'**
  String get signOut;

  /// No description provided for @signInFailed.
  ///
  /// In en, this message translates to:
  /// **'Sign-in was cancelled or failed.'**
  String get signInFailed;

  /// No description provided for @noAccount.
  ///
  /// In en, this message translates to:
  /// **'You are signed in, but there is no SmartRail account for you.'**
  String get noAccount;

  /// No description provided for @backToGuest.
  ///
  /// In en, this message translates to:
  /// **'Back to guest'**
  String get backToGuest;

  /// No description provided for @language.
  ///
  /// In en, this message translates to:
  /// **'Language'**
  String get language;

  /// No description provided for @theme.
  ///
  /// In en, this message translates to:
  /// **'Theme'**
  String get theme;

  /// No description provided for @themeSystem.
  ///
  /// In en, this message translates to:
  /// **'System'**
  String get themeSystem;

  /// No description provided for @themeLight.
  ///
  /// In en, this message translates to:
  /// **'Light'**
  String get themeLight;

  /// No description provided for @themeDark.
  ///
  /// In en, this message translates to:
  /// **'Dark'**
  String get themeDark;

  /// No description provided for @preferencesNote.
  ///
  /// In en, this message translates to:
  /// **'Saved on this device only.'**
  String get preferencesNote;

  /// No description provided for @statusSample.
  ///
  /// In en, this message translates to:
  /// **'Status sample'**
  String get statusSample;

  /// No description provided for @homeNextTrains.
  ///
  /// In en, this message translates to:
  /// **'Your next trains'**
  String get homeNextTrains;

  /// No description provided for @loadingStations.
  ///
  /// In en, this message translates to:
  /// **'Loading stations…'**
  String get loadingStations;

  /// No description provided for @stationsUnavailable.
  ///
  /// In en, this message translates to:
  /// **'Station list unavailable. Try again when connected.'**
  String get stationsUnavailable;

  /// No description provided for @savedJourneyMissing.
  ///
  /// In en, this message translates to:
  /// **'The saved timetable does not include this train. Reconnect to load the full route.'**
  String get savedJourneyMissing;

  /// No description provided for @trainHeader.
  ///
  /// In en, this message translates to:
  /// **'Trains'**
  String get trainHeader;

  /// No description provided for @trainIntro.
  ///
  /// In en, this message translates to:
  /// **'Compare scheduled times with SmartRail simulation evidence. Timetable and running information are separate.'**
  String get trainIntro;

  /// No description provided for @routeFrom.
  ///
  /// In en, this message translates to:
  /// **'From'**
  String get routeFrom;

  /// No description provided for @routeTo.
  ///
  /// In en, this message translates to:
  /// **'To'**
  String get routeTo;

  /// No description provided for @savedOffline.
  ///
  /// In en, this message translates to:
  /// **'Saved timetable · offline. Scheduled times only; no live estimates or platform overrides.'**
  String get savedOffline;

  /// No description provided for @coldOffline.
  ///
  /// In en, this message translates to:
  /// **'Offline and no saved timetable. Connect to download public train information.'**
  String get coldOffline;

  /// No description provided for @liveRefreshFailed.
  ///
  /// In en, this message translates to:
  /// **'Live train information could not refresh. Showing the last successful response.'**
  String get liveRefreshFailed;

  /// No description provided for @partialSchedule.
  ///
  /// In en, this message translates to:
  /// **'Live information loaded; the saved timetable could not refresh.'**
  String get partialSchedule;

  /// No description provided for @noFutureTrains.
  ///
  /// In en, this message translates to:
  /// **'No future scheduled trains for this route in the saved timetable.'**
  String get noFutureTrains;

  /// No description provided for @noTrains.
  ///
  /// In en, this message translates to:
  /// **'No trains are scheduled for this route in the current service window.'**
  String get noTrains;

  /// No description provided for @confirmedChanges.
  ///
  /// In en, this message translates to:
  /// **'Confirmed service changes'**
  String get confirmedChanges;

  /// No description provided for @savedNotices.
  ///
  /// In en, this message translates to:
  /// **'Saved confirmed notices · unverified'**
  String get savedNotices;

  /// No description provided for @browseAllTrains.
  ///
  /// In en, this message translates to:
  /// **'Browse all trains'**
  String get browseAllTrains;

  /// No description provided for @journeyDetails.
  ///
  /// In en, this message translates to:
  /// **'Journey details'**
  String get journeyDetails;

  /// No description provided for @routeDiagram.
  ///
  /// In en, this message translates to:
  /// **'Route · scheduled stops'**
  String get routeDiagram;

  /// No description provided for @scheduleSource.
  ///
  /// In en, this message translates to:
  /// **'Public timetable'**
  String get scheduleSource;

  /// No description provided for @noSavedJourney.
  ///
  /// In en, this message translates to:
  /// **'The saved timetable does not include this train. Reconnect to load the full route.'**
  String get noSavedJourney;

  /// No description provided for @notServed.
  ///
  /// In en, this message translates to:
  /// **'Not served'**
  String get notServed;

  /// No description provided for @simulatedRunning.
  ///
  /// In en, this message translates to:
  /// **'Running information, when available, is a SmartRail simulation.'**
  String get simulatedRunning;
}

class _AppLocalizationsDelegate
    extends LocalizationsDelegate<AppLocalizations> {
  const _AppLocalizationsDelegate();

  @override
  Future<AppLocalizations> load(Locale locale) {
    return SynchronousFuture<AppLocalizations>(lookupAppLocalizations(locale));
  }

  @override
  bool isSupported(Locale locale) =>
      <String>['en', 'si', 'ta'].contains(locale.languageCode);

  @override
  bool shouldReload(_AppLocalizationsDelegate old) => false;
}

AppLocalizations lookupAppLocalizations(Locale locale) {
  // Lookup logic when only language code is specified.
  switch (locale.languageCode) {
    case 'en':
      return AppLocalizationsEn();
    case 'si':
      return AppLocalizationsSi();
    case 'ta':
      return AppLocalizationsTa();
  }

  throw FlutterError(
    'AppLocalizations.delegate failed to load unsupported locale "$locale". This is likely '
    'an issue with the localizations generation tool. Please file an issue '
    'on GitHub with a reproducible sample app and the gen-l10n configuration '
    'that was used.',
  );
}
