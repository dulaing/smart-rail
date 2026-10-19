import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:smartrail_core/smartrail_core.dart';

Widget _host(Widget child, {SrProfile profile = SrProfile.passenger, Brightness brightness = Brightness.light, double width = 360}) {
  return MaterialApp(
    theme: srTheme(profile, brightness),
    home: Scaffold(
      body: Center(child: SizedBox(width: width, child: child)),
    ),
  );
}

final _now = DateTime.utc(2026, 10, 15, 2, 30);
String _utcClock(DateTime t) => '${t.toUtc().hour.toString().padLeft(2, '0')}:${t.toUtc().minute.toString().padLeft(2, '0')}';

void main() {
  group('describeFreshness', () {
    FreshnessDescription describe({DateTime? observed, DateTime? saved, SrConnection connection = SrConnection.online, String locale = 'en'}) =>
        describeFreshness(
          observedAt: observed,
          now: _now,
          locale: locale,
          savedAt: saved,
          connection: connection,
          formatTime: _utcClock,
        );

    test('current evidence says simulated and when it was updated', () {
      final d = describe(observed: _now.subtract(const Duration(seconds: 30)));
      expect(d.evidence, SrEvidence.current);
      expect(d.text, 'Simulated · updated 02:29');
    });

    test('ageing evidence is labelled ageing, not current', () {
      final d = describe(observed: _now.subtract(const Duration(seconds: 200)));
      expect(d.evidence, SrEvidence.ageing);
      expect(d.text, startsWith('Ageing'));
    });

    test('240 seconds is unknown and keeps the last update time', () {
      final d = describe(observed: _now.subtract(const Duration(seconds: 240)));
      expect(d.evidence, SrEvidence.unknown);
      expect(d.text, 'Position unknown · last update 02:26');
    });

    test('no evidence says there has been no update, never on time', () {
      final d = describe();
      expect(d.evidence, SrEvidence.unknown);
      expect(d.messageKey, 'freshness.none');
    });

    test('a saved copy states the download time and makes no claim about the position', () {
      final d = describe(observed: _now, saved: _now.subtract(const Duration(minutes: 12)));
      expect(d.evidence, isNull);
      expect(d.text, 'Saved copy · downloaded 02:18');
    });

    test('offline adds to the evidence wording instead of replacing it', () {
      final d = describe(observed: _now.subtract(const Duration(seconds: 200)), connection: SrConnection.offline);
      expect(d.evidence, SrEvidence.ageing);
      expect(d.text, 'Ageing · last update 02:26 · Offline');
    });

    test('every locale has wording for every message', () {
      for (final locale in srLocales) {
        for (final key in srMessages.keys) {
          expect(srMessages[key]![locale], isNotEmpty, reason: '$key / $locale');
        }
      }
    });
  });

  group('StatusBadge', () {
    testWidgets('shows text and an icon, and exposes the text to screen readers', (tester) async {
      final handle = tester.ensureSemantics();
      await tester.pumpWidget(_host(const StatusBadge(status: SrStatus.delayed, locale: 'en')));

      expect(find.text('Delayed'), findsOneWidget);
      expect(find.byIcon(Icons.schedule), findsOneWidget);
      expect(find.bySemanticsLabel('Delayed'), findsOneWidget);
      handle.dispose();
    });

    testWidgets('each status uses a different icon, so colour never carries the meaning', (tester) async {
      final icons = {for (final s in SrStatus.values) s.icon};
      expect(icons.length, SrStatus.values.length);
      final evidenceIcons = {for (final e in SrEvidence.values) e.icon};
      expect(evidenceIcons.length, SrEvidence.values.length);
    });

    testWidgets('cancelled and ends-early read differently', (tester) async {
      expect(SrStatus.cancelled.text['en'], isNot(SrStatus.endsAtStation.text['en']));
      expect(SrStatus.cancelled.icon, isNot(SrStatus.endsAtStation.icon));
    });

    testWidgets('renders Sinhala and Tamil text from the shared wording', (tester) async {
      await tester.pumpWidget(_host(Column(children: [
        StatusBadge.fromKey('cancelled', locale: 'si'),
        StatusBadge.fromKey('AGEING', locale: 'ta'),
      ])));
      expect(find.text(SrStatus.cancelled.text['si']!), findsOneWidget);
      expect(find.text(SrEvidence.ageing.text['ta']!), findsOneWidget);
    });

    testWidgets('an unknown key is rejected', (tester) async {
      expect(() => StatusBadge.fromKey('on-schedule', locale: 'en'), throwsArgumentError);
    });

    testWidgets('long Sinhala and Tamil wording wraps in a narrow space without overflow', (tester) async {
      await tester.pumpWidget(_host(
        SingleChildScrollView(
          child: Column(children: [
            for (final locale in srLocales) Text(srSampleLongLabel[locale]!),
            for (final locale in srLocales) StatusBadge(status: SrStatus.endsAtStation, locale: locale),
          ]),
        ),
        width: 140,
      ));
      expect(tester.takeException(), isNull);
    });
  });

  group('FreshnessLine', () {
    testWidgets('renders the wording for the evidence age', (tester) async {
      await tester.pumpWidget(_host(FreshnessLine(
        observedAt: _now.subtract(const Duration(seconds: 200)),
        now: _now,
        locale: 'en',
      )));
      expect(find.textContaining('Ageing'), findsOneWidget);
    });

    testWidgets('survives 200% text without overflow', (tester) async {
      await tester.pumpWidget(MaterialApp(
        theme: srTheme(SrProfile.driver, Brightness.light),
        builder: (context, child) => MediaQuery(
          data: MediaQuery.of(context).copyWith(textScaler: const TextScaler.linear(2)),
          child: child!,
        ),
        home: Scaffold(
          body: SizedBox(
            width: 300,
            child: FreshnessLine(observedAt: null, now: _now, locale: 'ta', connection: SrConnection.offline),
          ),
        ),
      ));
      expect(tester.takeException(), isNull);
    });
  });

  group('theme', () {
    test('driver has 20sp body text, 28sp times and 56dp targets', () {
      final metrics = srMetrics(SrProfile.driver);
      expect(metrics.type['body']!.size, 20);
      expect(metrics.type['time']!.size, 28);
      expect(metrics.minTarget, 56);
      expect(metrics.textContrast, 7);
    });

    test('station and driver are light only', () {
      expect(srProfileThemes[SrProfile.station], [Brightness.light]);
      expect(srProfileThemes[SrProfile.driver], [Brightness.light]);
      expect(srProfileThemes[SrProfile.ops], [Brightness.light]);
      expect(srProfileThemes[SrProfile.passenger], [Brightness.light, Brightness.dark]);
    });

    test('Sinhala text gets a taller line than English', () {
      final en = srTheme(SrProfile.passenger, Brightness.light).textTheme.bodyMedium!.height!;
      final si = srTheme(SrProfile.passenger, Brightness.light, locale: const Locale('si')).textTheme.bodyMedium!.height!;
      expect(si, greaterThan(en));
    });
  });
}
