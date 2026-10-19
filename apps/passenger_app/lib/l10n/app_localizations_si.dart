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
}
