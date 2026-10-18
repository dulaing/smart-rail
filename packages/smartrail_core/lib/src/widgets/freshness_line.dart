import 'package:flutter/material.dart';

import '../freshness.dart';
import '../generated/status.g.dart';
import '../theme.dart';
import 'status_badge.dart';

enum SrConnection { online, offline }

/// What a [FreshnessLine] says and which tone and icon it uses. Pure, so it can be tested alone.
class FreshnessDescription {
  const FreshnessDescription({
    required this.evidence,
    required this.messageKey,
    required this.text,
    required this.tone,
    required this.icon,
  });

  /// The evidence class, or null for a saved copy, which describes a download rather than a position.
  final SrEvidence? evidence;
  final String messageKey;
  final String text;
  final SrTone tone;
  final String icon;
}

String _clock(DateTime time) {
  final local = time.toLocal();
  String two(int n) => n.toString().padLeft(2, '0');
  return '${two(local.hour)}:${two(local.minute)}';
}

/// Chooses the honest wording for a piece of running information.
///
/// A [savedAt] time means the person is looking at a saved copy, so it says when that copy was
/// downloaded and claims nothing about the train's current position. Otherwise the wording
/// follows the evidence age. Being offline adds to the line; it does not replace the evidence.
FreshnessDescription describeFreshness({
  required DateTime? observedAt,
  required DateTime now,
  required String locale,
  SrConnection connection = SrConnection.online,
  DateTime? savedAt,
  int ageingAfterSeconds = srFreshnessFallbackAgeingSeconds,
  int unknownAfterSeconds = srFreshnessFallbackUnknownSeconds,
  String Function(DateTime time) formatTime = _clock,
}) {
  String message(String key, [DateTime? time]) {
    final text = srMessages[key]![locale] ?? srMessages[key]!['en']!;
    return time == null ? text : text.replaceAll('{time}', formatTime(time));
  }

  late String key;
  late SrTone tone;
  SrEvidence? evidence;
  DateTime? shownTime;

  if (savedAt != null) {
    key = 'freshness.saved';
    tone = SrTone.neutral;
    shownTime = savedAt;
  } else {
    evidence = freshnessOf(
      observedAt: observedAt,
      now: now,
      ageingAfterSeconds: ageingAfterSeconds,
      unknownAfterSeconds: unknownAfterSeconds,
    );
    tone = evidence.tone;
    switch (evidence) {
      case SrEvidence.current:
        key = 'freshness.current';
        shownTime = observedAt;
      case SrEvidence.ageing:
        key = 'freshness.ageing';
        shownTime = observedAt;
      case SrEvidence.unknown:
        key = observedAt == null ? 'freshness.none' : 'freshness.unknown';
        shownTime = observedAt;
    }
  }

  var text = message(key, shownTime);
  var icon = srMessageIcons[key]!;
  if (connection == SrConnection.offline) {
    text = '$text · ${message('freshness.offline')}';
    if (savedAt == null && evidence == SrEvidence.unknown) icon = srMessageIcons['freshness.offline']!;
  }
  return FreshnessDescription(evidence: evidence, messageKey: key, text: text, tone: tone, icon: icon);
}

/// One line saying how fresh the simulated running information is.
///
/// It is given every input and fetches nothing, polls nothing and owns no cache. [now] must
/// already be corrected with server time. Confirmed changes and their times are a separate fact
/// and do not belong here.
class FreshnessLine extends StatelessWidget {
  const FreshnessLine({
    super.key,
    required this.observedAt,
    required this.now,
    required this.locale,
    this.connection = SrConnection.online,
    this.savedAt,
    this.ageingAfterSeconds = srFreshnessFallbackAgeingSeconds,
    this.unknownAfterSeconds = srFreshnessFallbackUnknownSeconds,
  });

  final DateTime? observedAt;
  final DateTime now;
  final String locale;
  final SrConnection connection;
  final DateTime? savedAt;
  final int ageingAfterSeconds;
  final int unknownAfterSeconds;

  @override
  Widget build(BuildContext context) {
    final theme = SrTheme.of(context);
    final description = describeFreshness(
      observedAt: observedAt,
      now: now,
      locale: locale,
      connection: connection,
      savedAt: savedAt,
      ageingAfterSeconds: ageingAfterSeconds,
      unknownAfterSeconds: unknownAfterSeconds,
    );
    final colors = srToneColors(theme.palette, description.tone);
    // Neutral and accent tones use the normal text colour; the others use their dark ink.
    final ink = description.tone == SrTone.neutral ? theme.palette.textSecondary : colors.ink;
    final style = theme.copyWith(languageCode: locale).textOf(const ['caption', 'small', 'body'], color: ink);

    return Semantics(
      container: true,
      label: description.text,
      child: ExcludeSemantics(
        child: Row(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Padding(
              padding: const EdgeInsets.only(top: 2),
              child: Icon(srIcon(description.icon), size: style.fontSize! * 1.2, color: ink),
            ),
            SizedBox(width: theme.metrics.space[8]! / 2 + 2),
            Flexible(child: Text(description.text, style: style, softWrap: true)),
          ],
        ),
      ),
    );
  }
}
