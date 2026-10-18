import 'package:flutter/material.dart';

import '../generated/status.g.dart';
import '../theme.dart';

/// One icon shape per icon key in status.json. The shapes differ, so colour alone never carries
/// the meaning. The web renderer draws the matching path from the same file.
const _icons = <String, IconData>{
  'check': Icons.check_circle_outline,
  'clock': Icons.schedule,
  'cross': Icons.cancel_outlined,
  'flag': Icons.flag_outlined,
  'swap': Icons.swap_horiz,
  'signal': Icons.sensors,
  'hourglass': Icons.hourglass_bottom,
  'question': Icons.help_outline,
  'offline': Icons.cloud_off_outlined,
};

IconData srIcon(String key) => _icons[key] ?? Icons.help_outline;

/// A status or evidence label: text plus an icon shape on a tinted background.
///
/// It only displays what it is given. It never works out a state, so an on-time badge appears
/// only when the caller has real evidence of it.
class StatusBadge extends StatelessWidget {
  const StatusBadge({super.key, required this.status, required this.locale})
    : evidence = null;

  const StatusBadge.evidence({super.key, required SrEvidence this.evidence, required this.locale})
    : status = null;

  /// Looks the badge up by the key used in the API and status.json, such as `delayed` or `AGEING`.
  factory StatusBadge.fromKey(String key, {Key? widgetKey, required String locale}) {
    for (final status in SrStatus.values) {
      if (status.key == key) return StatusBadge(key: widgetKey, status: status, locale: locale);
    }
    for (final evidence in SrEvidence.values) {
      if (evidence.key == key) return StatusBadge.evidence(key: widgetKey, evidence: evidence, locale: locale);
    }
    throw ArgumentError.value(key, 'key', 'Unknown status or evidence key.');
  }

  final SrStatus? status;
  final SrEvidence? evidence;

  /// `en`, `si` or `ta`.
  final String locale;

  SrTone get _tone => status?.tone ?? evidence!.tone;
  String get _icon => status?.icon ?? evidence!.icon;
  String get _text => (status?.text ?? evidence!.text)[locale] ?? (status?.text ?? evidence!.text)['en']!;

  @override
  Widget build(BuildContext context) {
    final theme = SrTheme.of(context);
    final colors = srToneColors(theme.palette, _tone);
    final style = theme.copyWith(languageCode: locale).textOf(const ['caption', 'small', 'body'], color: colors.ink);
    final gap = theme.metrics.space[8]!;

    return Semantics(
      container: true,
      label: _text,
      child: ExcludeSemantics(
        child: DecoratedBox(
          decoration: BoxDecoration(
            color: colors.bg,
            border: Border.all(color: colors.edge, width: 1.5),
            borderRadius: BorderRadius.circular(theme.metrics.radius['full']!),
          ),
          child: Padding(
            padding: EdgeInsets.symmetric(horizontal: gap + 4, vertical: gap / 2 + 2),
            child: Row(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.center,
              children: [
                Icon(srIcon(_icon), size: style.fontSize! * 1.2, color: colors.ink),
                SizedBox(width: gap / 2 + 2),
                Flexible(child: Text(_text, style: style, softWrap: true)),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
