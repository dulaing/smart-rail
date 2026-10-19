// ignore: unused_import
import 'package:intl/intl.dart' as intl;

import 'app_localizations.dart';

// ignore_for_file: type=lint

/// The translations for Tamil (`ta`).
class AppLocalizationsTa extends AppLocalizations {
  AppLocalizationsTa([String locale = 'ta']) : super(locale);

  @override
  String get scheduledArrivalOrigin => 'திட்டமிட்ட வருகை நேரம்';

  @override
  String get scheduledDeparture => 'திட்டமிட்ட புறப்பாடு';

  @override
  String get expectedDeparture => 'எதிர்பார்க்கப்படும் புறப்பாடு';

  @override
  String get scheduledArrival => 'திட்டமிட்ட வருகை';

  @override
  String get expectedArrival => 'எதிர்பார்க்கப்படும் வருகை';

  @override
  String get plannedPlatform => 'திட்டமிட்ட நடைமேடை';

  @override
  String get platformUnavailable => 'நடைமேடை குறிப்பிடப்படவில்லை';

  @override
  String get platformConfirmed => 'பணியாளர் உறுதிப்படுத்திய நடைமேடை';

  @override
  String get overdueUnknown =>
      'திட்டமிட்ட புறப்பாட்டு நேரம் கடந்துவிட்டது; முன்னேற்றம் தெரியவில்லை.';

  @override
  String get endsEarly =>
      'இந்த ரயில் முன்கூட்டியே முடிகிறது. பயண விவரத்தில் கடைசி நிறுத்தத்தைப் பாருங்கள்.';

  @override
  String get serviceDate => 'சேவை தேதி';

  @override
  String get timetableSource => 'அட்டவணை மூலம்';

  @override
  String get publicRailwayTimetable => 'பொது ரயில் அட்டவணை';

  @override
  String get scheduledStops => 'வழி · திட்டமிட்ட நிறுத்தங்கள்';

  @override
  String get cancelledNotice => 'ரத்து செய்யப்பட்டுள்ளது';

  @override
  String get endsAt => 'முன்கூட்டியே முடிகிறது';

  @override
  String staffConfirmedChange(Object time) {
    return 'பணியாளர் உறுதிப்படுத்திய மாற்றம் · $time';
  }

  @override
  String get timeUnavailable => 'நேரம் கிடைக்கவில்லை';

  @override
  String get arrivalShort => 'வருகை';

  @override
  String get departureShort => 'புறப்பாடு';

  @override
  String savedNoticeDetails(Object time) {
    return 'சேமித்த அறிவிப்பு · சரிபார்க்கப்படவில்லை · $time';
  }

  @override
  String savedDownloadedAt(Object time) {
    return 'பதிவிறக்கிய நேரம் $time';
  }

  @override
  String get appTitle => 'SmartRail';

  @override
  String get tabHome => 'முகப்பு';

  @override
  String get tabTrains => 'ரயில்கள்';

  @override
  String get tabFood => 'உணவு';

  @override
  String get tabAccount => 'கணக்கு';

  @override
  String get navLabel => 'முதன்மை வழிசெலுத்தல்';

  @override
  String get comingSoon => 'விரைவில்';

  @override
  String get guest => 'விருந்தினர்';

  @override
  String get guestNotice =>
      'கணக்கு இல்லாமலே கால அட்டவணைகளையும் ரயில் நேரங்களையும் பார்க்கலாம்.';

  @override
  String get signIn => 'உள்நுழை';

  @override
  String get signOut => 'வெளியேறு';

  @override
  String get signInFailed =>
      'உள்நுழைவு ரத்துசெய்யப்பட்டது அல்லது தோல்வியடைந்தது.';

  @override
  String get noAccount =>
      'நீங்கள் உள்நுழைந்துள்ளீர்கள், ஆனால் உங்களுக்கு SmartRail கணக்கு இல்லை.';

  @override
  String get backToGuest => 'விருந்தினராகத் திரும்பு';

  @override
  String get language => 'மொழி';

  @override
  String get theme => 'தோற்றம்';

  @override
  String get themeSystem => 'கணினி';

  @override
  String get themeLight => 'வெளிச்சம்';

  @override
  String get themeDark => 'இருள்';

  @override
  String get preferencesNote => 'இந்தச் சாதனத்தில் மட்டும் சேமிக்கப்படும்.';

  @override
  String get statusSample => 'நிலை மாதிரி';

  @override
  String get homeNextTrains => 'உங்கள் அடுத்த ரயில்கள்';

  @override
  String get loadingStations => 'ரயில் நிலையங்கள் ஏற்றப்படுகின்றன…';

  @override
  String get stationsUnavailable =>
      'நிலையப் பட்டியல் கிடைக்கவில்லை. இணைப்பு கிடைத்ததும் மீண்டும் முயற்சிக்கவும்.';

  @override
  String get savedJourneyMissing =>
      'சேமித்த அட்டவணையில் இந்த ரயில் இல்லை. முழு வழியை ஏற்ற மீண்டும் இணைக்கவும்.';

  @override
  String get trainHeader => 'ரயில்கள்';

  @override
  String get trainIntro =>
      'அட்டவணை நேரங்களை SmartRail உருவக ஓட்டத் தகவலுடன் ஒப்பிடுங்கள். அட்டவணையும் ஓட்டத் தகவலும் தனித்தனியாகக் காட்டப்படும்.';

  @override
  String get routeFrom => 'இருந்து';

  @override
  String get routeTo => 'வரை';

  @override
  String get savedOffline =>
      'சேமித்த அட்டவணை · இணையமின்றி. அட்டவணை நேரங்கள் மட்டும்; நேரடி கணிப்புகளோ நடைமேடை மாற்றங்களோ இல்லை.';

  @override
  String get coldOffline =>
      'இணையமில்லை; சேமித்த அட்டவணையும் இல்லை. பொது ரயில் தகவலைப் பதிவிறக்க இணைக்கவும்.';

  @override
  String get liveRefreshFailed =>
      'நேரடி ரயில் தகவலைப் புதுப்பிக்க முடியவில்லை. கடைசியாக வெற்றிகரமாகப் பெற்ற தகவல் காட்டப்படுகிறது.';

  @override
  String get partialSchedule =>
      'நேரடி தகவல் கிடைத்தது; சேமித்த அட்டவணையைப் புதுப்பிக்க முடியவில்லை.';

  @override
  String get noFutureTrains =>
      'சேமித்த அட்டவணையில் இந்த வழிக்கான அடுத்த ரயில்கள் இல்லை.';

  @override
  String get noTrains =>
      'தற்போதைய சேவை காலத்தில் இந்த வழிக்கான ரயில்கள் இல்லை.';

  @override
  String get confirmedChanges => 'உறுதிப்படுத்திய சேவை மாற்றங்கள்';

  @override
  String get savedNotices =>
      'சேமித்த உறுதிப்படுத்திய அறிவிப்புகள் · சரிபார்க்கப்படவில்லை';

  @override
  String get browseAllTrains => 'அனைத்து ரயில்களையும் பாருங்கள்';

  @override
  String get journeyDetails => 'பயண விவரங்கள்';

  @override
  String get routeDiagram => 'வழி · திட்டமிட்ட நிறுத்தங்கள்';

  @override
  String get scheduleSource => 'பொது அட்டவணை';

  @override
  String get noSavedJourney =>
      'சேமித்த அட்டவணையில் இந்த ரயில் இல்லை. முழு வழியைப் பெற மீண்டும் இணைக்கவும்.';

  @override
  String get notServed => 'சேவை இல்லை';

  @override
  String get simulatedRunning =>
      'ஓட்டத் தகவல் கிடைத்தால், அது SmartRail உருவகத் தகவல்.';

  @override
  String get foodTitle => 'உணவு';

  @override
  String get foodIntro =>
      'உங்கள் ரயில் நிறுத்தத்தில் பெற்றுக்கொள்ள உணவை ஆர்டர் செய்யுங்கள். பெறும்போது கியோஸ்க்கில் பணம் செலுத்துங்கள்.';

  @override
  String get foodChooseTrain => 'ரயிலைத் தேர்ந்தெடுக்கவும்';

  @override
  String get foodChangeTrain => 'ரயிலை மாற்றவும்';

  @override
  String get foodNoTrainChosen =>
      'உணவைப் பெறக்கூடிய இடங்களைப் பார்க்க தேதியுடன் கூடிய ரயிலைத் தேர்ந்தெடுக்கவும்.';

  @override
  String foodTrainLine(Object train, Object from, Object to) {
    return '$train · $from → $to';
  }

  @override
  String get foodToday => 'இன்று';

  @override
  String get foodOnRoute => 'உங்கள் ரயில் பாதையில்';

  @override
  String get foodNoTrains =>
      'வரவிருக்கும் ரயில்கள் இன்னும் சேமிக்கப்படவில்லை. அட்டவணையை ஏற்ற இணைப்பில் இருக்கும்போது ரயில்கள் பகுதியைத் திறக்கவும்.';

  @override
  String get foodPickupHeading => 'பெறும் இடங்கள்';

  @override
  String foodArrives(Object time) {
    return 'ரயில் வருகை $time';
  }

  @override
  String foodScheduledAt(Object time) {
    return 'திட்டமிட்டது $time';
  }

  @override
  String get foodNoEstimate => 'வருகை மதிப்பீடு இல்லை';

  @override
  String foodOrderBy(Object time) {
    return '$timeக்குள் ஆர்டர் செய்யுங்கள்';
  }

  @override
  String get foodSeeMenu => 'மெனுவைப் பார்க்கவும்';

  @override
  String get foodNoPickups => 'இந்த ரயிலில் உணவு பெறும் இடங்கள் இல்லை.';

  @override
  String get foodUnavailableHeading => 'ஆர்டர் செய்ய முடியாது';

  @override
  String get foodReasonJourneyNotToday =>
      'இந்த ரயில் இன்று இயங்காததால் இப்போது உணவை ஆர்டர் செய்ய முடியாது.';

  @override
  String get foodReasonStopNotServed =>
      'இந்த ரயில் இந்த நிறுத்தத்தில் நிற்காது.';

  @override
  String get foodReasonStopPassed =>
      'ரயில் ஏற்கனவே இந்த நிறுத்தத்தைக் கடந்துவிட்டது.';

  @override
  String get foodReasonTrainUnknown =>
      'இந்த ரயிலின் சமீபத்திய இருப்பிடம் SmartRail இடம் இல்லாததால் பெறும் நேரத்தை உறுதியளிக்க முடியாது.';

  @override
  String get foodReasonGeneric => 'இப்போது இங்கே உணவை ஆர்டர் செய்ய முடியாது.';

  @override
  String get foodProviderUnavailable =>
      'உணவு ஆர்டர் இப்போது கிடைக்கவில்லை. ரயில் தகவல் பாதிக்கப்படவில்லை.';

  @override
  String get foodLoadFailed =>
      'உணவுத் தகவலை ஏற்ற முடியவில்லை. இணைப்பைச் சரிபார்த்து மீண்டும் முயலவும்.';

  @override
  String get foodJourneyNotFound =>
      'இந்த ரயிலை SmartRail இனி அறியவில்லை. வேறொன்றைத் தேர்ந்தெடுக்கவும்.';

  @override
  String get foodRetry => 'மீண்டும் முயலவும்';

  @override
  String get foodStaleOptions =>
      'புதுப்பிக்க முடியவில்லை. இந்த விருப்பங்கள் காலாவதியானவையாக இருக்கலாம்.';

  @override
  String foodMenuHeading(Object point) {
    return 'மெனு · $point';
  }

  @override
  String get foodMenuEmpty => 'இந்த பெறும் இடத்தில் இப்போது பொருட்கள் இல்லை.';

  @override
  String get foodSoldOut => 'தீர்ந்துவிட்டது';

  @override
  String foodAddItem(Object item) {
    return '$item ஒன்றைச் சேர்க்கவும்';
  }

  @override
  String foodRemoveItem(Object item) {
    return '$item ஒன்றை நீக்கவும்';
  }

  @override
  String foodQuantityOf(Object item, int count) {
    return '$item: கூடையில் $count';
  }

  @override
  String foodBasketSummary(int count, Object total) {
    return '$count பொருட்கள் · $total';
  }

  @override
  String get foodReview => 'ஆர்டரை மதிப்பாய்வு செய்';

  @override
  String get foodBasketAdjusted =>
      'மெனு மாறியதால் உங்கள் கூடை புதுப்பிக்கப்பட்டது.';

  @override
  String get foodPickupUnavailableNow =>
      'இந்த பெறுதலை இனி ஆர்டர் செய்ய முடியாது.';

  @override
  String get foodBack => 'பெறும் இடங்களுக்குத் திரும்பு';
}
