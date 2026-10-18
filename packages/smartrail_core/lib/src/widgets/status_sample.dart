import 'package:flutter/material.dart';

import '../generated/status.g.dart';
import '../theme.dart';
import 'freshness_line.dart';
import 'status_badge.dart';

/// A development-only page that shows every badge and freshness state in English, Sinhala and
/// Tamil, plus a long label, so the Flutter and web renderers can be compared side by side.
/// Apps open it only in debug builds. It uses no live data and is not a feature screen.
class StatusSample extends StatelessWidget {
  const StatusSample({super.key, this.now});

  /// The clock the samples age against; defaults to the device time.
  final DateTime? now;

  @override
  Widget build(BuildContext context) {
    final theme = SrTheme.of(context);
    final now = this.now ?? DateTime.now();
    final gap = theme.metrics.space[16]!;
    final heading = theme.textOf(const ['heading', 'title']);

    Widget section(String title, List<Widget> children) => Padding(
      padding: EdgeInsets.only(bottom: gap * 1.5),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Semantics(header: true, child: Text(title, style: heading)),
          SizedBox(height: gap / 2),
          ...children,
        ],
      ),
    );

    return Scaffold(
      appBar: AppBar(title: const Text('Status sample (development)')),
      body: ListView(
        padding: EdgeInsets.all(gap),
        children: [
          Text(
            'Sinhala and Tamil wording is $srTranslationReview: no native reader has checked it.',
            style: theme.textOf(const ['caption', 'small', 'body'], color: theme.palette.textSecondary),
          ),
          SizedBox(height: gap),
          for (final locale in srLocales)
            section('Train facts · $locale', [
              Wrap(
                spacing: gap / 2,
                runSpacing: gap / 2,
                children: [for (final s in SrStatus.values) StatusBadge(status: s, locale: locale)],
              ),
              SizedBox(height: gap / 2),
              Wrap(
                spacing: gap / 2,
                runSpacing: gap / 2,
                children: [for (final e in SrEvidence.values) StatusBadge.evidence(evidence: e, locale: locale)],
              ),
            ]),
          for (final locale in srLocales)
            section('Freshness · $locale', [
              FreshnessLine(observedAt: now.subtract(const Duration(seconds: 30)), now: now, locale: locale),
              FreshnessLine(observedAt: now.subtract(const Duration(seconds: 200)), now: now, locale: locale),
              FreshnessLine(observedAt: now.subtract(const Duration(seconds: 300)), now: now, locale: locale),
              FreshnessLine(observedAt: null, now: now, locale: locale),
              FreshnessLine(
                observedAt: now.subtract(const Duration(seconds: 30)),
                now: now,
                locale: locale,
                savedAt: now.subtract(const Duration(minutes: 12)),
                connection: SrConnection.offline,
              ),
            ]),
          for (final locale in srLocales)
            section('Long label · $locale', [
              Text(srSampleLongLabel[locale]!, style: theme.copyWith(languageCode: locale).textOf(const ['body'])),
            ]),
        ],
      ),
    );
  }
}
