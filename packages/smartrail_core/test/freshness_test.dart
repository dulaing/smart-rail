import 'dart:convert';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:smartrail_core/smartrail_core.dart';

// Same fixture as the C# tests and shared/design/freshness.test.ts. `flutter test` runs from
// the package folder.
const _fixturePath = '../../tests/SmartRail.Api.Tests/Rail/freshness-vectors.json';

void main() {
  final fixture = jsonDecode(File(_fixturePath).readAsStringSync()) as Map<String, dynamic>;
  final ageing = fixture['ageingAfterSeconds'] as int;
  final unknown = fixture['unknownAfterSeconds'] as int;
  final cases = (fixture['cases'] as List).cast<Map<String, dynamic>>();

  group('freshness fixture', () {
    for (final c in cases) {
      test(c['name'] as String, () {
        final observed = c['observedAt'] == null ? null : DateTime.parse(c['observedAt'] as String);
        final result = freshnessOf(
          observedAt: observed,
          now: DateTime.parse(c['now'] as String),
          ageingAfterSeconds: ageing,
          unknownAfterSeconds: unknown,
        );
        expect(result.key, c['freshness']);
      });
    }

    test('covers the 179, 180, 239 and 240 second boundaries and missing evidence', () {
      final names = cases.map((c) => c['name']);
      expect(names, containsAll(['179 seconds old', '180 seconds old', '239 seconds old', '240 seconds old', 'no observation']));
    });
  });

  test('built-in fallback thresholds match the fixture', () {
    expect(srFreshnessFallbackAgeingSeconds, ageing);
    expect(srFreshnessFallbackUnknownSeconds, unknown);
  });

  test('a server-corrected clock keeps ageing between polls', () {
    final offset = clockOffset(
      serverTime: DateTime.parse('2026-10-15T08:00:00+05:30'),
      deviceTimeAtResponse: DateTime.parse('2026-10-15T07:58:30+05:30'),
    );
    expect(offset, const Duration(seconds: 90));
    final deviceNow = DateTime.parse('2026-10-15T08:01:30+05:30'); // really 08:03:00
    expect(
      freshnessOf(observedAt: DateTime.parse('2026-10-15T08:00:00+05:30'), now: deviceNow.add(offset)),
      SrEvidence.ageing,
    );
  });
}
