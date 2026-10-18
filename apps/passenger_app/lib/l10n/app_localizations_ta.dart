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
}
