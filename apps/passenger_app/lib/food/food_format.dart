import 'package:intl/intl.dart';

import '../l10n/app_localizations.dart';

/// Sri Lanka has no daylight saving, so Colombo time is a fixed UTC+05:30. Food times are shown
/// there whatever the phone's own time zone is, like the rest of the passenger app.
DateTime colomboTime(DateTime instant) => instant.toUtc().add(const Duration(hours: 5, minutes: 30));

String clockTime(DateTime instant) => DateFormat.Hm().format(colomboTime(instant));

String dayLabel(DateTime serviceDate) => DateFormat.MMMEd().format(serviceDate);

/// The wording for a stable refusal or cancellation `code`. A code this app does not know gets a
/// plain generic sentence rather than being shown raw or treated as success.
String foodReasonText(AppLocalizations t, String? code) => switch (code) {
  'JOURNEY_NOT_TODAY' => t.foodReasonJourneyNotToday,
  'STOP_NOT_SERVED' => t.foodReasonStopNotServed,
  'STOP_PASSED' => t.foodReasonStopPassed,
  'TRAIN_UNKNOWN' => t.foodReasonTrainUnknown,
  _ => t.foodReasonGeneric,
};
