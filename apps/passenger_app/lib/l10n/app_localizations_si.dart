// ignore: unused_import
import 'package:intl/intl.dart' as intl;

import 'app_localizations.dart';

// ignore_for_file: type=lint

/// The translations for Sinhala Sinhalese (`si`).
class AppLocalizationsSi extends AppLocalizations {
  AppLocalizationsSi([String locale = 'si']) : super(locale);

  @override
  String get scheduledArrivalOrigin => 'නියමිත ආගමන වේලාව';

  @override
  String get scheduledDeparture => 'නියමිත පිටත්වීම';

  @override
  String get expectedDeparture => 'අපේක්ෂිත පිටත්වීම';

  @override
  String get scheduledArrival => 'නියමිත ආගමනය';

  @override
  String get expectedArrival => 'අපේක්ෂිත ආගමනය';

  @override
  String get plannedPlatform => 'සැලසුම් කළ වේදිකාව';

  @override
  String get platformUnavailable => 'වේදිකා අංකයක් නැත';

  @override
  String get platformConfirmed => 'කාර්ය මණ්ඩලය තහවුරු කළ වේදිකාව';

  @override
  String get overdueUnknown =>
      'නියමිත පිටත්වීමේ වේලාව පසු වී ඇත; ගමන් තත්ත්වය නොදනී.';

  @override
  String get endsEarly =>
      'මෙම දුම්රිය කලින් අවසන් වේ. ගමන් විස්තරයේ අවසන් නැවතුම බලන්න.';

  @override
  String get serviceDate => 'සේවා දිනය';

  @override
  String get timetableSource => 'කාලසටහන් මූලාශ්‍රය';

  @override
  String get publicRailwayTimetable => 'පොදු දුම්රිය කාලසටහන';

  @override
  String get scheduledStops => 'මාර්ගය · නියමිත නැවතුම්';

  @override
  String get cancelledNotice => 'අවලංගු කර ඇත';

  @override
  String get endsAt => 'කලින් අවසන් වේ';

  @override
  String staffConfirmedChange(Object time) {
    return 'කාර්ය මණ්ඩලය තහවුරු කළ වෙනස · $time';
  }

  @override
  String get timeUnavailable => 'වේලාව නොදනී';

  @override
  String get arrivalShort => 'පැමිණීම';

  @override
  String get departureShort => 'පිටත්වීම';

  @override
  String savedNoticeDetails(Object time) {
    return 'සුරැකි දැනුම්දීම · තහවුරු නොකළ · $time';
  }

  @override
  String savedDownloadedAt(Object time) {
    return 'බාගත කළේ $time';
  }

  @override
  String get appTitle => 'SmartRail';

  @override
  String get tabHome => 'මුල් පිටුව';

  @override
  String get tabTrains => 'දුම්රිය';

  @override
  String get tabFood => 'ආහාර';

  @override
  String get tabAccount => 'ගිණුම';

  @override
  String get navLabel => 'ප්‍රධාන සංචලනය';

  @override
  String get comingSoon => 'ඉක්මනින්';

  @override
  String get guest => 'අමුත්තා';

  @override
  String get guestNotice =>
      'ගිණුමක් නොමැතිව කාලසටහන් සහ දුම්රිය වේලාවන් බැලිය හැක.';

  @override
  String get signIn => 'පිවිසෙන්න';

  @override
  String get signOut => 'ඉවත් වන්න';

  @override
  String get signInFailed => 'පිවිසීම අවලංගු විය හෝ අසාර්ථක විය.';

  @override
  String get noAccount => 'ඔබ පිවිසී ඇත, නමුත් ඔබට SmartRail ගිණුමක් නැත.';

  @override
  String get backToGuest => 'අමුත්තා ලෙස ආපසු';

  @override
  String get language => 'භාෂාව';

  @override
  String get theme => 'තේමාව';

  @override
  String get themeSystem => 'පද්ධතිය';

  @override
  String get themeLight => 'ලා';

  @override
  String get themeDark => 'අඳුරු';

  @override
  String get preferencesNote => 'මෙම උපාංගයේ පමණක් සුරැකේ.';

  @override
  String get statusSample => 'තත්ත්ව නිදර්ශනය';

  @override
  String get homeNextTrains => 'ඔබගේ මීළඟ දුම්රියන්';

  @override
  String get loadingStations => 'දුම්රිය ස්ථාන පූරණය වෙමින්…';

  @override
  String get stationsUnavailable =>
      'දුම්රිය ස්ථාන ලැයිස්තුව ලබාගත නොහැක. සම්බන්ධ වූ විට නැවත උත්සාහ කරන්න.';

  @override
  String get savedJourneyMissing =>
      'සුරැකි කාලසටහනේ මෙම දුම්රිය නැත. සම්පූර්ණ මාර්ගය පූරණය කිරීමට නැවත සම්බන්ධ වන්න.';

  @override
  String get trainHeader => 'දුම්රිය';

  @override
  String get trainIntro =>
      'කාලසටහන් වේලාවන් SmartRail අනුකරණ ධාවන තොරතුරු සමඟ සසඳන්න. කාලසටහන සහ ධාවන තොරතුරු වෙන වෙනම පෙන්වයි.';

  @override
  String get routeFrom => 'සිට';

  @override
  String get routeTo => 'දක්වා';

  @override
  String get savedOffline =>
      'සුරැකි කාලසටහන · නොබැඳි. කාලසටහන් වේලාවන් පමණි; සජීවී ඇස්තමේන්තු හෝ වේදිකා වෙනස්කම් නැත.';

  @override
  String get coldOffline =>
      'නොබැඳි අතර සුරැකි කාලසටහනක් නැත. පොදු දුම්රිය තොරතුරු බාගැනීමට සම්බන්ධ වන්න.';

  @override
  String get liveRefreshFailed =>
      'සජීවී දුම්රිය තොරතුරු යාවත්කාලීන කළ නොහැක. අවසන් සාර්ථක තොරතුරු පෙන්වයි.';

  @override
  String get partialSchedule =>
      'සජීවී තොරතුරු ලැබුණි; සුරැකි කාලසටහන යාවත්කාලීන කළ නොහැක.';

  @override
  String get noFutureTrains => 'සුරැකි කාලසටහනේ මෙම මාර්ගයට ඉදිරි දුම්රිය නැත.';

  @override
  String get noTrains => 'වත්මන් සේවා කාලය තුළ මෙම මාර්ගයට දුම්රිය නැත.';

  @override
  String get confirmedChanges => 'තහවුරු කළ සේවා වෙනස්කම්';

  @override
  String get savedNotices => 'සුරැකි වෙනස්කම් දැනුම්දීම් · තහවුරු නොකළ';

  @override
  String get browseAllTrains => 'සියලු දුම්රිය බලන්න';

  @override
  String get journeyDetails => 'ගමන් විස්තර';

  @override
  String get routeDiagram => 'මාර්ගය · නියමිත නැවතුම්';

  @override
  String get scheduleSource => 'පොදු කාලසටහන';

  @override
  String get noSavedJourney =>
      'සුරැකි කාලසටහනේ මෙම දුම්රිය නැත. සම්පූර්ණ මාර්ගය ලබා ගැනීමට නැවත සම්බන්ධ වන්න.';

  @override
  String get notServed => 'සේවය නොකරයි';

  @override
  String get simulatedRunning => 'ධාවන තොරතුරු ඇත්නම් ඒවා SmartRail අනුකරණයකි.';

  @override
  String get foodTitle => 'ආහාර';

  @override
  String get foodIntro =>
      'ඔබේ දුම්රිය නැවතුමකදී ලබාගැනීමට ආහාර ඇණවුම් කරන්න. ලබාගන්නා විට කියෝස්කුවේදී ගෙවන්න.';

  @override
  String get foodChooseTrain => 'දුම්රියක් තෝරන්න';

  @override
  String get foodChangeTrain => 'දුම්රිය වෙනස් කරන්න';

  @override
  String get foodNoTrainChosen =>
      'ආහාර ලබාගත හැකි තැන් බැලීමට දිනයක් සහිත දුම්රියක් තෝරන්න.';

  @override
  String foodTrainLine(Object train, Object from, Object to) {
    return '$train · $from → $to';
  }

  @override
  String get foodToday => 'අද';

  @override
  String get foodOnRoute => 'ඔබේ දුම්රිය මාර්ගයේ';

  @override
  String get foodNoTrains =>
      'ඉදිරි දුම්රිය තවම සුරැකී නැත. කාලසටහන පූරණය කිරීමට සම්බන්ධව සිට දුම්රිය පිටුව විවෘත කරන්න.';

  @override
  String get foodPickupHeading => 'ලබාගත හැකි තැන්';

  @override
  String foodArrives(Object time) {
    return 'දුම්රිය පැමිණීම $time';
  }

  @override
  String foodScheduledAt(Object time) {
    return 'නියමිත $time';
  }

  @override
  String get foodNoEstimate => 'පැමිණීමේ ඇස්තමේන්තුවක් නැත';

  @override
  String foodOrderBy(Object time) {
    return '$time ට පෙර ඇණවුම් කරන්න';
  }

  @override
  String get foodSeeMenu => 'ආහාර ලැයිස්තුව බලන්න';

  @override
  String get foodNoPickups => 'මෙම දුම්රියේ ආහාර ලබාගැනීමේ ස්ථාන නැත.';

  @override
  String get foodUnavailableHeading => 'ඇණවුම් කළ නොහැක';

  @override
  String get foodReasonJourneyNotToday =>
      'මෙම දුම්රිය අද ධාවනය නොවන බැවින් තවම ආහාර ඇණවුම් කළ නොහැක.';

  @override
  String get foodReasonStopNotServed => 'මෙම දුම්රිය මෙම නැවතුමේ නතර නොවේ.';

  @override
  String get foodReasonStopPassed => 'දුම්රිය දැනටමත් මෙම නැවතුම පසු කර ඇත.';

  @override
  String get foodReasonTrainUnknown =>
      'මෙම දුම්රියේ මෑත ස්ථානයක් SmartRail සතුව නැති බැවින් ලබාගැනීමේ වේලාවක් ලබා දිය නොහැක.';

  @override
  String get foodReasonGeneric => 'දැන් මෙහි ආහාර ඇණවුම් කළ නොහැක.';

  @override
  String get foodProviderUnavailable =>
      'ආහාර ඇණවුම් දැන් ලබාගත නොහැක. දුම්රිය තොරතුරු කිසිවක් වෙනස් නොවේ.';

  @override
  String get foodLoadFailed =>
      'ආහාර තොරතුරු පූරණය කළ නොහැකි විය. සම්බන්ධතාව පරීක්ෂා කර නැවත උත්සාහ කරන්න.';

  @override
  String get foodJourneyNotFound =>
      'SmartRail හට මෙම දුම්රිය තවදුරටත් නොදනී. වෙනත් එකක් තෝරන්න.';

  @override
  String get foodRetry => 'නැවත උත්සාහ කරන්න';

  @override
  String get foodStaleOptions =>
      'නැවුම් කළ නොහැකි විය. මෙම විකල්ප යල් පැන ගිය ඒවා විය හැක.';

  @override
  String foodMenuHeading(Object point) {
    return 'ආහාර ලැයිස්තුව · $point';
  }

  @override
  String get foodMenuEmpty => 'මෙම ලබාගැනීමේ ස්ථානයේ දැන් ලබාගත හැකි ආහාර නැත.';

  @override
  String get foodSoldOut => 'අවසන්';

  @override
  String foodAddItem(Object item) {
    return '$item එකක් එක් කරන්න';
  }

  @override
  String foodRemoveItem(Object item) {
    return '$item එකක් ඉවත් කරන්න';
  }

  @override
  String foodQuantityOf(Object item, int count) {
    return '$item: කූඩයේ $count';
  }

  @override
  String foodBasketSummary(int count, Object total) {
    return 'අයිතම $count · $total';
  }

  @override
  String get foodReview => 'ඇණවුම සමාලෝචනය';

  @override
  String get foodBasketAdjusted =>
      'ආහාර ලැයිස්තුව වෙනස් වූ බැවින් ඔබේ කූඩය යාවත්කාලීන කරන ලදී.';

  @override
  String get foodPickupUnavailableNow => 'මෙම ලබාගැනීම දැන් ඇණවුම් කළ නොහැක.';

  @override
  String get foodBack => 'ලබාගැනීමේ ස්ථාන වෙත ආපසු';

  @override
  String get foodReviewHeading => 'ඔබේ ඇණවුම සමාලෝචනය කරන්න';

  @override
  String foodCollectAt(Object point) {
    return '$point හිදී ලබාගන්න';
  }

  @override
  String foodTrainExpected(Object time) {
    return 'දුම්රිය අපේක්ෂිත වේලාව $time. මෙය SmartRail ඇස්තමේන්තුවකි; වෙනස් විය හැක.';
  }

  @override
  String get foodPayAtKiosk =>
      'ලබාගන්නා විට කියෝස්කුවේදී ගෙවන්න. මාර්ගගත ගෙවීමක් නැත.';

  @override
  String get foodNoShowPolicy =>
      'නොපැමිණීම: ඔබ ඇණවුම ලබා නොගන්නේ නම්, දුම්රිය පිටත් වූ පසු සැපයුම්කරු එය නොපැමිණීම ලෙස සලකුණු කරයි.';

  @override
  String get foodCancelPolicy =>
      'සැපයුම්කරු තවමත් අවලංගු කිරීම් පිළිගන්නා අතරතුර පමණක් අවලංගු කළ හැක. ඔබ අවලංගු කරන විට SmartRail මෙය පරීක්ෂා කරයි.';

  @override
  String get foodTotal => 'මුළු මුදල';

  @override
  String get foodSignInToOrder => 'ඇණවුම් කිරීමට පිවිසෙන්න';

  @override
  String get foodSignInNote =>
      'ගිණුමක් නොමැතිව බ්‍රවුස් කළ හැක. ඔබ පිවිසෙන අතරතුර ඔබේ කූඩය මෙහි රැඳේ.';

  @override
  String get foodSignInCancelled =>
      'පිවිසීම අවලංගු විය. ඔබේ කූඩය තවමත් මෙහි ඇත.';

  @override
  String get foodNoPermission => 'ඔබේ ගිණුමට ආහාර ඇණවුම් කළ නොහැක.';

  @override
  String get foodPlaceOrder => 'ඇණවුම තබන්න';

  @override
  String get foodBackToMenu => 'ආහාර ලැයිස්තුවට ආපසු';

  @override
  String get foodSubmitting => 'ඔබේ ඇණවුම තබමින්…';

  @override
  String get foodUncertainTitle => 'ඇණවුමේ ප්‍රතිඵලය තවම තහවුරු වී නැත';

  @override
  String get foodUncertainBody =>
      'ආහාර සැපයුම්කරුගෙන් තවම පිළිතුරක් ලැබී නැත. ඔබේ ඇණවුම එක් යොමුවක් යටතේ සුරැකී ඇති බැවින් නැවත පරීක්ෂා කිරීමෙන් දෙවන ඇණවුමක් නොතැබේ. ඔබට මෙම තිරයෙන් ඉවත් වී නැවත පැමිණිය හැක.';

  @override
  String get foodCheckAgain => 'නැවත පරීක්ෂා කරන්න';

  @override
  String get foodChecking => 'පරීක්ෂා කරමින්…';

  @override
  String get foodConflictBody =>
      'මෙම ඉල්ලීම් යොමුව දැනටමත් වෙනත් ඇණවුමක් සඳහා භාවිත කර ඇතැයි SmartRail පවසයි. කිසිවක් වෙනස් නොවීය. නැවත උත්සාහ කිරීමට පෙර ඔබේ ඇණවුම් පරීක්ෂා කරන්න.';

  @override
  String get foodDiscardRequest => 'මෙම ඉල්ලීම ඉවත දමන්න';

  @override
  String get foodRefusedTitle => 'ඇණවුම තබා නැත';

  @override
  String get foodRefusedBack => 'කූඩයට ආපසු';

  @override
  String get foodReasonCutoffPassed =>
      'මෙම ලබාගැනීම සඳහා ඇණවුම් කිරීමේ අවසන් වේලාව ඉකුත් වී ඇත.';

  @override
  String get foodReasonStockInsufficient =>
      'ඔබේ කූඩයේ ඇති යම් අයිතමයක් සඳහා තොග ප්‍රමාණවත් නොවේ.';

  @override
  String get foodReasonItemUnavailable =>
      'ඔබේ කූඩයේ ඇති අයිතමයක් තවදුරටත් ලබාගත නොහැක.';

  @override
  String get foodReasonPickupMismatch =>
      'මෙම ලබාගැනීම තවදුරටත් දුම්රියට ගැලපෙන්නේ නැත. නැවත තෝරන්න.';

  @override
  String get foodReasonOrderNotFound =>
      'SmartRail හට මෙම ඇණවුම සොයාගත නොහැකි විය.';

  @override
  String get foodReasonPassengerCancelled => 'ඔබ මෙම ඇණවුම අවලංගු කළා.';

  @override
  String get foodReasonTrainCancelled =>
      'දුම්රිය අවලංගු කළ බැවින් මෙම ඇණවුම අවලංගු විය.';

  @override
  String get foodReasonTrainEndedEarly =>
      'දුම්රිය මෙම නැවතුමට පෙර අවසන් වන බැවින් මෙම ඇණවුම අවලංගු විය.';

  @override
  String get foodSessionEnded =>
      'ඔබේ පිවිසුම අවසන් විය. ඔබේ ඇණවුම බැලීමට නැවත පිවිසෙන්න. ඔබ පිවිසෙන තුරු කිසිවක් පෙන්වන්නේ නැත.';

  @override
  String get foodStorageFailed =>
      'ඔබේ ඇණවුම මෙම දුරකථනයේ සුරැකීමට නොහැකි වූ බැවින් එය යවා නැත. නැවත උත්සාහ කරන්න.';

  @override
  String get foodOrderConfirmed => 'ඇණවුම තහවුරු විය';

  @override
  String get foodPickupCode => 'ලබාගැනීමේ කේතය';

  @override
  String get foodPickupCodeHint =>
      'මෙම කේතය කියෝස්කුවේදී පෙන්වන්න. එය මෙහි ඔබට පමණක් පෙන්වයි.';

  @override
  String foodEvidenceAt(Object time) {
    return '$time වේලාවේ දුම්රිය සාක්ෂි අනුව ඇස්තමේන්තුව';
  }

  @override
  String get foodStatusPlacing => 'තබමින්';

  @override
  String get foodStatusRejected => 'තබා නැත';

  @override
  String get foodStatusAccepted => 'පිළිගත්තා';

  @override
  String get foodStatusReady => 'ලබාගැනීමට සූදානම්';

  @override
  String get foodStatusCollected => 'ලබාගත්තා';

  @override
  String get foodStatusNoShow => 'නොපැමිණීම';

  @override
  String get foodStatusCancelled => 'අවලංගු කළා';

  @override
  String get foodYourOrder => 'ඔබේ ඇණවුම';

  @override
  String get foodOrderDone => 'අවසන්';

  @override
  String get foodOpenOrder => 'ඔබේ ඇණවුම බලන්න';

  @override
  String get foodCancelOrder => 'ඇණවුම අවලංගු කරන්න';

  @override
  String get foodCancelConfirmTitle => 'මෙම ඇණවුම අවලංගු කරන්නද?';

  @override
  String get foodCancelConfirmBody =>
      'SmartRail ආහාර සැපයුම්කරුගෙන් විමසයි. අවසන් වේලාවෙන් පසු වැනි අවස්ථාවල එය ප්‍රතික්ෂේප කළ හැක.';

  @override
  String get foodKeepOrder => 'ඇණවුම තබාගන්න';

  @override
  String get foodCancelling => 'අවලංගු කරමින්…';

  @override
  String get foodCancelUnavailable =>
      'ආහාර සැපයුම්කරු වෙත ළඟා විය නොහැකි විය. කිසිවක් වෙනස් නොවීය; නැවත උත්සාහ කරන්න.';

  @override
  String get foodCancelUnconfirmed =>
      'අවලංගු කිරීම සිදු වූවාදැයි තහවුරු කළ නොහැකි විය. පෙන්වන ඇණවුම SmartRail අවසන් වරට වාර්තා කළ දෙයයි. දෙවන වරට උත්සාහ කිරීමට පෙර නැවත පරීක්ෂා කරන්න.';

  @override
  String get foodReasonInvalidState => 'මෙම ඇණවුම තවදුරටත් අවලංගු කළ නොහැක.';

  @override
  String get foodReasonPlacementPending =>
      'ඇණවුම තවමත් තබමින් පවතී. ටික වේලාවකින් නැවත උත්සාහ කරන්න.';

  @override
  String get foodRecentOrders => 'ඔබේ ඇණවුම්';

  @override
  String foodOrderPlacedAt(Object time) {
    return '$time ට තැබුවා';
  }

  @override
  String get foodPendingOrderWaiting =>
      'ඔබට තහවුරු වීමට බලා සිටින ඇණවුමක් ඇති බැවින් තවම නව ඇණවුමක් තැබිය නොහැක.';

  @override
  String get foodBackToFood => 'ආහාර වෙත ආපසු';

  @override
  String get foodOrderHistory => 'ඇණවුම් ඉතිහාසය';
}
