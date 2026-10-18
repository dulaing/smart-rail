// ignore: unused_import
import 'package:intl/intl.dart' as intl;

import 'app_localizations.dart';

// ignore_for_file: type=lint

/// The translations for Tamil (`ta`).
class AppLocalizationsTa extends AppLocalizations {
  AppLocalizationsTa([String locale = 'ta']) : super(locale);

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
}
