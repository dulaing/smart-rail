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

  /// No description provided for @foodTitle.
  ///
  /// In en, this message translates to:
  /// **'Food'**
  String get foodTitle;

  /// No description provided for @foodIntro.
  ///
  /// In en, this message translates to:
  /// **'Order food to collect at a station stop on your train. You pay at the kiosk when you collect.'**
  String get foodIntro;

  /// No description provided for @foodChooseTrain.
  ///
  /// In en, this message translates to:
  /// **'Choose a train'**
  String get foodChooseTrain;

  /// No description provided for @foodChangeTrain.
  ///
  /// In en, this message translates to:
  /// **'Change train'**
  String get foodChangeTrain;

  /// No description provided for @foodNoTrainChosen.
  ///
  /// In en, this message translates to:
  /// **'Pick a dated train to see where you can collect food.'**
  String get foodNoTrainChosen;

  /// No description provided for @foodTrainLine.
  ///
  /// In en, this message translates to:
  /// **'{train} · {from} → {to}'**
  String foodTrainLine(Object train, Object from, Object to);

  /// No description provided for @foodToday.
  ///
  /// In en, this message translates to:
  /// **'Today'**
  String get foodToday;

  /// No description provided for @foodOnRoute.
  ///
  /// In en, this message translates to:
  /// **'On your Trains route'**
  String get foodOnRoute;

  /// No description provided for @foodNoTrains.
  ///
  /// In en, this message translates to:
  /// **'No upcoming trains are saved yet. Open Trains while connected to load the timetable.'**
  String get foodNoTrains;

  /// No description provided for @foodPickupHeading.
  ///
  /// In en, this message translates to:
  /// **'Where to collect'**
  String get foodPickupHeading;

  /// No description provided for @foodArrives.
  ///
  /// In en, this message translates to:
  /// **'Train due {time}'**
  String foodArrives(Object time);

  /// No description provided for @foodScheduledAt.
  ///
  /// In en, this message translates to:
  /// **'Scheduled {time}'**
  String foodScheduledAt(Object time);

  /// No description provided for @foodNoEstimate.
  ///
  /// In en, this message translates to:
  /// **'No arrival estimate'**
  String get foodNoEstimate;

  /// No description provided for @foodOrderBy.
  ///
  /// In en, this message translates to:
  /// **'Order by {time}'**
  String foodOrderBy(Object time);

  /// No description provided for @foodSeeMenu.
  ///
  /// In en, this message translates to:
  /// **'See menu'**
  String get foodSeeMenu;

  /// No description provided for @foodNoPickups.
  ///
  /// In en, this message translates to:
  /// **'This train has no food collection points.'**
  String get foodNoPickups;

  /// No description provided for @foodUnavailableHeading.
  ///
  /// In en, this message translates to:
  /// **'Not available to order'**
  String get foodUnavailableHeading;

  /// No description provided for @foodReasonJourneyNotToday.
  ///
  /// In en, this message translates to:
  /// **'This train does not run today, so food cannot be ordered yet.'**
  String get foodReasonJourneyNotToday;

  /// No description provided for @foodReasonStopNotServed.
  ///
  /// In en, this message translates to:
  /// **'This train does not serve this stop.'**
  String get foodReasonStopNotServed;

  /// No description provided for @foodReasonStopPassed.
  ///
  /// In en, this message translates to:
  /// **'The train has already passed this stop.'**
  String get foodReasonStopPassed;

  /// No description provided for @foodReasonTrainUnknown.
  ///
  /// In en, this message translates to:
  /// **'SmartRail has no recent position for this train, so a pickup time cannot be promised.'**
  String get foodReasonTrainUnknown;

  /// No description provided for @foodReasonGeneric.
  ///
  /// In en, this message translates to:
  /// **'Food cannot be ordered here right now.'**
  String get foodReasonGeneric;

  /// No description provided for @foodProviderUnavailable.
  ///
  /// In en, this message translates to:
  /// **'Food ordering is unavailable right now. Train information is not affected.'**
  String get foodProviderUnavailable;

  /// No description provided for @foodLoadFailed.
  ///
  /// In en, this message translates to:
  /// **'Could not load food information. Check your connection and try again.'**
  String get foodLoadFailed;

  /// No description provided for @foodJourneyNotFound.
  ///
  /// In en, this message translates to:
  /// **'SmartRail no longer knows this train. Choose another one.'**
  String get foodJourneyNotFound;

  /// No description provided for @foodRetry.
  ///
  /// In en, this message translates to:
  /// **'Try again'**
  String get foodRetry;

  /// No description provided for @foodStaleOptions.
  ///
  /// In en, this message translates to:
  /// **'Could not refresh. These options may be out of date.'**
  String get foodStaleOptions;

  /// No description provided for @foodMenuHeading.
  ///
  /// In en, this message translates to:
  /// **'Menu · {point}'**
  String foodMenuHeading(Object point);

  /// No description provided for @foodMenuEmpty.
  ///
  /// In en, this message translates to:
  /// **'This collection point has no items available right now.'**
  String get foodMenuEmpty;

  /// No description provided for @foodSoldOut.
  ///
  /// In en, this message translates to:
  /// **'Sold out'**
  String get foodSoldOut;

  /// No description provided for @foodAddItem.
  ///
  /// In en, this message translates to:
  /// **'Add one {item}'**
  String foodAddItem(Object item);

  /// No description provided for @foodRemoveItem.
  ///
  /// In en, this message translates to:
  /// **'Remove one {item}'**
  String foodRemoveItem(Object item);

  /// No description provided for @foodQuantityOf.
  ///
  /// In en, this message translates to:
  /// **'{item}: {count} in basket'**
  String foodQuantityOf(Object item, int count);

  /// No description provided for @foodBasketSummary.
  ///
  /// In en, this message translates to:
  /// **'{count, plural, =1{1 item} other{{count} items}} · {total}'**
  String foodBasketSummary(int count, Object total);

  /// No description provided for @foodReview.
  ///
  /// In en, this message translates to:
  /// **'Review order'**
  String get foodReview;

  /// No description provided for @foodBasketAdjusted.
  ///
  /// In en, this message translates to:
  /// **'The menu changed, so your basket was updated.'**
  String get foodBasketAdjusted;

  /// No description provided for @foodPickupUnavailableNow.
  ///
  /// In en, this message translates to:
  /// **'This pickup is no longer available to order.'**
  String get foodPickupUnavailableNow;

  /// No description provided for @foodBack.
  ///
  /// In en, this message translates to:
  /// **'Back to pickup points'**
  String get foodBack;

  /// No description provided for @foodReviewHeading.
  ///
  /// In en, this message translates to:
  /// **'Review your order'**
  String get foodReviewHeading;

  /// No description provided for @foodCollectAt.
  ///
  /// In en, this message translates to:
  /// **'Collect at {point}'**
  String foodCollectAt(Object point);

  /// No description provided for @foodTrainExpected.
  ///
  /// In en, this message translates to:
  /// **'Train expected {time}. This is a SmartRail estimate and can change.'**
  String foodTrainExpected(Object time);

  /// No description provided for @foodPayAtKiosk.
  ///
  /// In en, this message translates to:
  /// **'Pay at the kiosk when you collect. There is no online payment.'**
  String get foodPayAtKiosk;

  /// No description provided for @foodNoShowPolicy.
  ///
  /// In en, this message translates to:
  /// **'No-show: if you do not collect your order, the provider marks it No-show after the train leaves.'**
  String get foodNoShowPolicy;

  /// No description provided for @foodCancelPolicy.
  ///
  /// In en, this message translates to:
  /// **'You can cancel only while the provider still accepts cancellations. SmartRail checks this when you cancel.'**
  String get foodCancelPolicy;

  /// No description provided for @foodTotal.
  ///
  /// In en, this message translates to:
  /// **'Total'**
  String get foodTotal;

  /// No description provided for @foodSignInToOrder.
  ///
  /// In en, this message translates to:
  /// **'Sign in to order'**
  String get foodSignInToOrder;

  /// No description provided for @foodSignInNote.
  ///
  /// In en, this message translates to:
  /// **'You can keep browsing without an account. Your basket stays here while you sign in.'**
  String get foodSignInNote;

  /// No description provided for @foodSignInCancelled.
  ///
  /// In en, this message translates to:
  /// **'Sign-in was cancelled. Your basket is still here.'**
  String get foodSignInCancelled;

  /// No description provided for @foodNoPermission.
  ///
  /// In en, this message translates to:
  /// **'Your account cannot place food orders.'**
  String get foodNoPermission;

  /// No description provided for @foodPlaceOrder.
  ///
  /// In en, this message translates to:
  /// **'Place order'**
  String get foodPlaceOrder;

  /// No description provided for @foodBackToMenu.
  ///
  /// In en, this message translates to:
  /// **'Back to menu'**
  String get foodBackToMenu;

  /// No description provided for @foodSubmitting.
  ///
  /// In en, this message translates to:
  /// **'Placing your order…'**
  String get foodSubmitting;

  /// No description provided for @foodUncertainTitle.
  ///
  /// In en, this message translates to:
  /// **'Order outcome not yet confirmed'**
  String get foodUncertainTitle;

  /// No description provided for @foodUncertainBody.
  ///
  /// In en, this message translates to:
  /// **'We have not heard back from the food provider yet. Your order is saved under one reference, so checking again never places a second order. You can leave this screen and come back.'**
  String get foodUncertainBody;

  /// No description provided for @foodCheckAgain.
  ///
  /// In en, this message translates to:
  /// **'Check again'**
  String get foodCheckAgain;

  /// No description provided for @foodChecking.
  ///
  /// In en, this message translates to:
  /// **'Checking…'**
  String get foodChecking;

  /// No description provided for @foodConflictBody.
  ///
  /// In en, this message translates to:
  /// **'SmartRail says this request reference was already used for a different order. Nothing was changed. Check your orders before trying again.'**
  String get foodConflictBody;

  /// No description provided for @foodDiscardRequest.
  ///
  /// In en, this message translates to:
  /// **'Discard this request'**
  String get foodDiscardRequest;

  /// No description provided for @foodRefusedTitle.
  ///
  /// In en, this message translates to:
  /// **'Order not placed'**
  String get foodRefusedTitle;

  /// No description provided for @foodRefusedBack.
  ///
  /// In en, this message translates to:
  /// **'Back to basket'**
  String get foodRefusedBack;

  /// No description provided for @foodReasonCutoffPassed.
  ///
  /// In en, this message translates to:
  /// **'The order cutoff for this pickup has passed.'**
  String get foodReasonCutoffPassed;

  /// No description provided for @foodReasonStockInsufficient.
  ///
  /// In en, this message translates to:
  /// **'There is not enough stock for something in your basket.'**
  String get foodReasonStockInsufficient;

  /// No description provided for @foodReasonItemUnavailable.
  ///
  /// In en, this message translates to:
  /// **'An item in your basket is no longer available.'**
  String get foodReasonItemUnavailable;

  /// No description provided for @foodReasonPickupMismatch.
  ///
  /// In en, this message translates to:
  /// **'This pickup no longer matches the train. Choose it again.'**
  String get foodReasonPickupMismatch;

  /// No description provided for @foodReasonOrderNotFound.
  ///
  /// In en, this message translates to:
  /// **'SmartRail could not find this order.'**
  String get foodReasonOrderNotFound;

  /// No description provided for @foodReasonPassengerCancelled.
  ///
  /// In en, this message translates to:
  /// **'You cancelled this order.'**
  String get foodReasonPassengerCancelled;

  /// No description provided for @foodReasonTrainCancelled.
  ///
  /// In en, this message translates to:
  /// **'The train was cancelled, so this order was cancelled.'**
  String get foodReasonTrainCancelled;

  /// No description provided for @foodReasonTrainEndedEarly.
  ///
  /// In en, this message translates to:
  /// **'The train ends before this stop, so this order was cancelled.'**
  String get foodReasonTrainEndedEarly;

  /// No description provided for @foodSessionEnded.
  ///
  /// In en, this message translates to:
  /// **'Your sign-in ended. Sign in again to see your order. Nothing is shown until you do.'**
  String get foodSessionEnded;

  /// No description provided for @foodStorageFailed.
  ///
  /// In en, this message translates to:
  /// **'Could not save your order on this phone, so it was not sent. Try again.'**
  String get foodStorageFailed;

  /// No description provided for @foodOrderConfirmed.
  ///
  /// In en, this message translates to:
  /// **'Order confirmed'**
  String get foodOrderConfirmed;

  /// No description provided for @foodPickupCode.
  ///
  /// In en, this message translates to:
  /// **'Pickup code'**
  String get foodPickupCode;

  /// No description provided for @foodPickupCodeHint.
  ///
  /// In en, this message translates to:
  /// **'Show this code at the kiosk. It is only shown here, to you.'**
  String get foodPickupCodeHint;

  /// No description provided for @foodEvidenceAt.
  ///
  /// In en, this message translates to:
  /// **'Estimate from train evidence at {time}'**
  String foodEvidenceAt(Object time);

  /// No description provided for @foodStatusPlacing.
  ///
  /// In en, this message translates to:
  /// **'Placing'**
  String get foodStatusPlacing;

  /// No description provided for @foodStatusRejected.
  ///
  /// In en, this message translates to:
  /// **'Not placed'**
  String get foodStatusRejected;

  /// No description provided for @foodStatusAccepted.
  ///
  /// In en, this message translates to:
  /// **'Accepted'**
  String get foodStatusAccepted;

  /// No description provided for @foodStatusReady.
  ///
  /// In en, this message translates to:
  /// **'Ready to collect'**
  String get foodStatusReady;

  /// No description provided for @foodStatusCollected.
  ///
  /// In en, this message translates to:
  /// **'Collected'**
  String get foodStatusCollected;

  /// No description provided for @foodStatusNoShow.
  ///
  /// In en, this message translates to:
  /// **'No-show'**
  String get foodStatusNoShow;

  /// No description provided for @foodStatusCancelled.
  ///
  /// In en, this message translates to:
  /// **'Cancelled'**
  String get foodStatusCancelled;

  /// No description provided for @foodYourOrder.
  ///
  /// In en, this message translates to:
  /// **'Your order'**
  String get foodYourOrder;

  /// No description provided for @foodOrderDone.
  ///
  /// In en, this message translates to:
  /// **'Done'**
  String get foodOrderDone;

  /// No description provided for @foodOpenOrder.
  ///
  /// In en, this message translates to:
  /// **'See your order'**
  String get foodOpenOrder;
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
