import 'dart:convert';
import 'dart:io';
import 'dart:ui' as ui;

import 'package:driver_app/journeys/briefing_api.dart';
import 'package:driver_app/journeys/report_api.dart';
import 'package:driver_app/journeys/report_view.dart';
import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:smartrail_core/smartrail_core.dart';

final _journey = DriverJourney.fromJson(
  jsonDecode(File('test/visual/fixtures/duty.json').readAsStringSync())
      as Map<String, dynamic>,
);
const _boundary = Key('report-capture');
final _received = DateTime.utc(2026, 10, 12, 1, 22);

DriverReport _report(bool ack) => DriverReport(
  id: 'RPT-118',
  clientReportId: 'client-118',
  journeyId: _journey.id,
  reasonCode: 'SIGNAL_DELAY',
  receivedAt: _received,
  state: ack ? 'ACKNOWLEDGED' : 'SENT_AWAITING_CONTROL',
  acknowledgedByName: ack ? 'Dilani' : null,
  acknowledgedAt: ack ? _received.add(const Duration(minutes: 3)) : null,
);

Future<void> _pump(
  WidgetTester tester,
  String state, {
  bool phone = false,
  double scale = 1,
  DriverJourney? journey,
}) async {
  final size = phone ? const Size(360, 800) : const Size(1280, 800);
  final dpr = phone ? 3.0 : 2.0;
  tester.view.devicePixelRatio = dpr;
  tester.view.physicalSize = size * dpr;
  addTearDown(tester.view.resetPhysicalSize);
  addTearDown(tester.view.resetDevicePixelRatio);
  final now = _received.add(
    Duration(
      minutes: state == 'compose'
          ? -1
          : state == 'ack'
          ? 3
          : state == 'await'
          ? 1
          : 0,
    ),
  );
  await tester.pumpWidget(
    MaterialApp(
      theme: srTheme(SrProfile.driver, Brightness.light),
      home: MediaQuery(
        data: MediaQueryData(
          size: size,
          devicePixelRatio: dpr,
          textScaler: TextScaler.linear(scale),
          padding: phone
              ? const EdgeInsets.only(top: 40, bottom: 20)
              : EdgeInsets.zero,
        ),
        child: RepaintBoundary(
          key: _boundary,
          child: DriverReportView(
            journey: journey ?? _journey,
            reason: 'SIGNAL_DELAY',
            displayName: 'Sunil',
            now: now,
            checkedAt: now,
            hasDraft: state == 'uncertain',
            report: state == 'await' || state == 'ack'
                ? _report(state == 'ack')
                : null,
            onBack: () {},
            onSend: () {},
            onCheck: () {},
            onReason: (_) {},
            onReports: () {},
            onAccount: () {},
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
      await (FontLoader('packages/smartrail_core/$family')
            ..addFont(rootBundle.load('packages/smartrail_core/fonts/$file')))
          .load();
    }
    await (FontLoader('DriverSymbols')
          ..addFont(rootBundle.load('assets/fonts/NotoSansMath-Regular.ttf')))
        .load();
  });
  for (final phone in [false, true]) {
    for (final state in ['compose', 'uncertain', 'await', 'ack']) {
      final name = '${phone ? 'phone' : 'tablet'}-report-$state';
      testWidgets('$name real-font reference geometry and capture', (
        tester,
      ) async {
        await _pump(tester, state, phone: phone);
        expect(tester.takeException(), isNull);
        final expected =
            (jsonDecode(
                  File(
                    'test/visual/comparisons/2026-10-19/report.reference-bounds.json',
                  ).readAsStringSync(),
                ) as Map)[name]
                as Map;
        final geometry = <String, Object>{};
        for (final entry in expected.entries) {
          final rect = tester.getRect(find.byKey(Key(entry.key as String)));
          final actual = {
            'x': rect.left,
            'y': rect.top,
            'width': rect.width,
            'height': rect.height,
          };
          geometry[entry.key as String] = actual;
          for (final field in actual.entries) {
            expect(
              field.value,
              closeTo((entry.value[field.key] as num).toDouble(), 1),
              reason: '$name ${entry.key}.${field.key}',
            );
          }
        }
        final boundary = tester.renderObject<RenderRepaintBoundary>(
          find.byKey(_boundary),
        );
        final bytes = await tester.runAsync(() async {
          final image = await boundary.toImage(pixelRatio: phone ? 3 : 2);
          final bytes = (await image.toByteData(
            format: ui.ImageByteFormat.png,
          ))!.buffer.asUint8List();
          await File('test/visual/comparisons/2026-10-19/$name.actual.png')
              .writeAsBytes(bytes);
          await File(
            'test/visual/comparisons/2026-10-19/$name.actual-bounds.json',
          ).writeAsString(const JsonEncoder.withIndent('  ').convert(geometry));
          image.dispose();
          return bytes;
        });
        await tester.runAsync(
          () => expectLater(bytes!, matchesGoldenFile('goldens/$name.png')),
        );
      });
      testWidgets('$name targets and large text reflow', (tester) async {
        final semantics = tester.ensureSemantics();
        await _pump(tester, state, phone: phone);
        await expectLater(tester, meetsGuideline(androidTapTargetGuideline));
        await expectLater(tester, meetsGuideline(labeledTapTargetGuideline));
        _checkContrast();
        semantics.dispose();
        await _pump(tester, state, phone: phone, scale: 2);
        expect(tester.takeException(), isNull);
        await tester.drag(
          find.byKey(const Key('report-scroll')),
          const Offset(0, -1800),
        );
        await tester.pumpAndSettle();
        expect(tester.takeException(), isNull);
      });
    }
  }

  testWidgets('long Sinhala and Tamil report context scrolls at 200 percent', (
    tester,
  ) async {
    final raw = _journey.toJson();
    raw['trainNumber'] = 'DS-B1 දුම්රිය ரயில்';
    raw['originName'] = 'වේයන්ගොඩ දුම්රිය ස්ථානය வியாங்கொடை நிலையம்';
    raw['destinationName'] =
        'කොළඹ කොටුව දුම්රිය ස්ථානය கொழும்பு கோட்டை நிலையம்';
    for (final state in ['compose', 'uncertain', 'ack']) {
      await _pump(
        tester,
        state,
        phone: true,
        scale: 2,
        journey: DriverJourney.fromJson(raw),
      );
      expect(tester.takeException(), isNull);
      await tester.drag(
        find.byKey(const Key('report-scroll')),
        const Offset(0, -2400),
      );
      await tester.pumpAndSettle();
      expect(tester.takeException(), isNull);
      expect(find.byKey(const Key('report-primary')), findsOneWidget);
    }
  });
}

void _checkContrast() {
  var checked = 0;
  for (final element in find.byType(RichText).evaluate()) {
    final rich = element.widget as RichText;
    final ink = rich.text.style?.color;
    if (ink == null) continue;
    Color? surface;
    element.visitAncestorElements((ancestor) {
      final color = switch (ancestor.widget) {
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
  expect(checked, greaterThan(8));
}
