import 'dart:convert';
import 'dart:io';
import 'dart:ui' as ui;

import 'package:driver_app/journeys/briefing_api.dart';
import 'package:driver_app/journeys/briefing_cache.dart';
import 'package:driver_app/journeys/briefing_view.dart';
import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:smartrail_core/smartrail_core.dart';

final _now = DateTime.utc(2026, 10, 12, 1, 38);
final _fixture = jsonDecode(
  File('test/visual/fixtures/duty.json').readAsStringSync(),
) as Map<String, dynamic>;
final _journey = DriverJourney.fromJson(_fixture);
DriverBriefing _briefing() => DriverBriefing.fromJson(
  jsonDecode(jsonEncode(_fixture)) as Map<String, dynamic>,
);
const _boundary = Key('capture');

Future<void> _pump(
  WidgetTester tester, {
  Size size = const Size(1280, 800),
  double dpr = 2,
  double scale = 1,
  bool saved = false,
  DriverBriefing? briefing,
  DateTime? now,
}) async {
  tester.view.devicePixelRatio = dpr;
  tester.view.physicalSize = size * dpr;
  addTearDown(tester.view.resetPhysicalSize);
  addTearDown(tester.view.resetDevicePixelRatio);
  await tester.pumpWidget(
    MaterialApp(
      theme: srTheme(SrProfile.driver, Brightness.light),
      home: MediaQuery(
        data: MediaQueryData(
          size: size,
          devicePixelRatio: dpr,
          textScaler: TextScaler.linear(scale),
          padding: size.width < 1000
              ? const EdgeInsets.only(top: 40, bottom: 20)
              : EdgeInsets.zero,
        ),
        child: RepaintBoundary(
          key: _boundary,
          child: DriverDutyView(
            journey: _journey,
            briefing: briefing ?? _briefing(),
            now: now ?? _now,
            displayName: 'Sunil',
            saved: saved,
            downloadedAt: _now,
            checkedAt: _now,
            onReport: () {},
            onAccount: () {},
            onBack: () {},
          ),
        ),
      ),
    ),
  );
  await tester.pumpAndSettle();
  await tester.runAsync(
    () => precacheImage(
      const AssetImage('assets/figma/driver-logo.png'),
      tester.element(find.byKey(_boundary)),
    ),
  );
  await tester.pumpAndSettle();
}

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  setUpAll(() async {
    for (final (family, file) in [
      ('NotoSans', 'NotoSans-Variable.ttf'),
      ('AbhayaLibre', 'AbhayaLibre-Regular.ttf'),
      ('NotoSansTamil', 'NotoSansTamil-Variable.ttf'),
    ]) {
      final loader = FontLoader('packages/smartrail_core/$family')
        ..addFont(rootBundle.load('packages/smartrail_core/fonts/$file'));
      await loader.load();
    }
    final symbols = FontLoader('DriverSymbols')
      ..addFont(rootBundle.load('assets/fonts/NotoSansMath-Regular.ttf'));
    await symbols.load();
  });

  for (final (name, size, dpr) in [
    ('tablet-duty', const Size(1280, 800), 2.0),
    ('phone-briefing', const Size(360, 800), 3.0),
  ]) {
    testWidgets('$name deterministic real-font capture', (tester) async {
      await _pump(tester, size: size, dpr: dpr);
      expect(tester.takeException(), isNull);
      final boundary = tester.renderObject<RenderRepaintBoundary>(
        find.byKey(_boundary),
      );
      final geometry = <String, Map<String, double>>{};
      if (name == 'tablet-duty') {
        final reference = jsonDecode(
          File(
            'test/visual/comparisons/2026-10-19/tablet-duty.reference-bounds.json',
          ).readAsStringSync(),
        ) as Map<String, dynamic>;
        for (final entry
            in (reference['bounds'] as Map<String, dynamic>).entries) {
          final found = find.byKey(Key(entry.key));
          expect(found, findsOneWidget, reason: entry.key);
          final rect = tester.getRect(found);
          final actual = {
            'x': rect.left,
            'y': rect.top,
            'width': rect.width,
            'height': rect.height,
          };
          geometry[entry.key] = actual;
          for (final field in actual.entries) {
            expect(
              field.value,
              closeTo((entry.value[field.key] as num).toDouble(), 1),
              reason: '${entry.key}.${field.key}',
            );
          }
        }
        expect(geometry.length, (reference['bounds'] as Map).length);
      }
      await tester.runAsync(() async {
        final image = await boundary.toImage(pixelRatio: dpr);
        final bytes = (await image.toByteData(format: ui.ImageByteFormat.png))!
            .buffer
            .asUint8List();
        final file = File(
          'test/visual/comparisons/2026-10-19/$name.actual.png',
        );
        await file.parent.create(recursive: true);
        await file.writeAsBytes(bytes);
        if (geometry.isNotEmpty) {
          await File(
            'test/visual/comparisons/2026-10-19/$name.actual-bounds.json',
          ).writeAsString(const JsonEncoder.withIndent('  ').convert(geometry));
        }
        image.dispose();
      });
      expect(
        tester.getRect(find.byKey(const Key('next-stop'))).width,
        closeTo(name == 'tablet-duty' ? 616 : 970 / 3, .01),
      );
    });
  }

  for (final size in [const Size(1280, 800), const Size(360, 800)]) {
    testWidgets(
      'selected briefing meets target, label and 7:1 paint contrast at $size',
      (tester) async {
        final handle = tester.ensureSemantics();
        await _pump(tester, size: size);
        await expectLater(tester, meetsGuideline(androidTapTargetGuideline));
        await expectLater(tester, meetsGuideline(labeledTapTargetGuideline));
        // Flutter's screenshot histogram can choose antialiased digit fringes as
        // the ink colour. Check every rendered text span's resolved opaque paint
        // against its actual nearest opaque surface instead, retaining captures
        // for the separate raster review.
        var checked = 0;
        for (final element in find.byType(RichText).evaluate()) {
          final rich = element.widget as RichText;
          final ink = rich.text.style?.color;
          if (ink == null) continue;
          Color? surface;
          element.visitAncestorElements((ancestor) {
            final w = ancestor.widget;
            final color = switch (w) {
              Material(:final color) => color,
              ColoredBox(:final color) => color,
              DecoratedBox(decoration: BoxDecoration(:final color)) => color,
              _ => null,
            };
            if (color != null && color.a == 1) {
              surface = color;
              return false;
            }
            return true;
          });
          final paper =
              surface ?? srPalette(SrProfile.driver, Brightness.light).bgCanvas;
          final a = ink.computeLuminance(), b = paper.computeLuminance();
          expect(
            ((a > b ? a : b) + .05) / ((a > b ? b : a) + .05),
            greaterThanOrEqualTo(7),
            reason: rich.text.toPlainText(),
          );
          checked++;
        }
        expect(checked, greaterThan(20));
        handle.dispose();
      },
    );
  }

  test('briefing cache keeps stable change and position identities', () {
    final original = _briefing();
    final restored = DriverBriefing.fromJson(original.toJson());
    expect(restored.changes.first.id, 'platform');
    expect(restored.changes.first.targetStopId, 'gampaha');
    expect(restored.positionToStopId, 'gampaha');
    expect(restored.positionProgressPermille, 600);
    expect(restored.stops.first.stationId, original.lastKnownStationId);
  });

  testWidgets('unknown and saved evidence suppress next-stop projections', (
    tester,
  ) async {
    for (final saved in [false, true]) {
      await _pump(
        tester,
        saved: saved,
        now: _now.add(const Duration(minutes: 5)),
      );
      expect(find.byKey(const Key('next-stop')), findsNothing);
      expect(find.text('19'), findsNothing);
      expect(find.text('7:27'), findsNothing);
      expect(find.text('Running 17 min late'), findsNothing);
      expect(find.text('Confirmed 17 min delay'), findsOneWidget);
      expect(
        find.text('Next stop unavailable. Scheduled stops are shown below.'),
        findsOneWidget,
      );
      expect(find.text('3'), findsOneWidget);
      expect(tester.takeException(), isNull);
    }
  });

  testWidgets('change detail re-resolves the stable ID after refresh', (
    tester,
  ) async {
    await _pump(tester);
    await tester.tap(find.text('Gampaha: Platform 3'));
    await tester.pumpAndSettle();
    expect(find.text('View all updates'), findsOneWidget);
    final raw = jsonDecode(jsonEncode(_fixture)) as Map<String, dynamic>;
    raw['changes'] = <dynamic>[];
    await _pump(tester, briefing: DriverBriefing.fromJson(raw));
    expect(
      find.text(
        'This change is no longer in the current briefing. The list has been refreshed.',
      ),
      findsOneWidget,
    );
    expect(find.text('Gampaha: Platform 3'), findsNothing);
  });

  testWidgets(
    'phone at 200% scrolls complete content and retains the report action',
    (tester) async {
      await _pump(tester, size: const Size(360, 800), dpr: 3, scale: 2);
      expect(tester.takeException(), isNull);
      expect(find.text('Report a problem'), findsOneWidget);
      expect(
        tester.getRect(find.text('Report a problem')).bottom,
        lessThanOrEqualTo(780),
      );
    },
  );
}
