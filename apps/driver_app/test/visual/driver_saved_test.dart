import 'dart:convert';
import 'dart:io';
import 'dart:ui' as ui;

import 'package:driver_app/journeys/briefing_api.dart';
import 'package:driver_app/journeys/briefing_view.dart';
import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:smartrail_core/smartrail_core.dart';

final _fixture = jsonDecode(
  File('test/visual/fixtures/duty.json').readAsStringSync(),
) as Map<String, dynamic>;
final _at = DateTime.utc(2026, 10, 12, 1, 35);
const _boundary = Key('saved-capture');

Future<void> _pump(
  WidgetTester tester, {
  bool phone = false,
  double scale = 1,
  DateTime? checkedAt,
  VoidCallback? reconnect,
  Future<void> Function()? refresh,
  Map<String, dynamic>? raw,
}) async {
  final size = phone ? const Size(360, 800) : const Size(1280, 800);
  final dpr = phone ? 3.0 : 2.0;
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
          padding: phone
              ? const EdgeInsets.only(top: 40, bottom: 20)
              : EdgeInsets.zero,
        ),
        child: RepaintBoundary(
          key: _boundary,
          child: DriverDutyView(
            journey: DriverJourney.fromJson(_fixture),
            briefing: DriverBriefing.fromJson(raw ?? _fixture),
            now: _at.add(const Duration(minutes: 10)),
            displayName: 'Sunil',
            saved: true,
            downloadedAt: _at,
            checkedAt: checkedAt ?? _at,
            onRefresh: refresh ?? () async {},
            onReconnect: reconnect,
            onAccount: () {},
            onReport: () {},
            onReports: () {},
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

void _checkContrast() {
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
  }
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
  final bounds = jsonDecode(
    File('test/visual/comparisons/2026-10-19/saved.reference-bounds.json')
        .readAsStringSync(),
  ) as Map;
  for (final phone in [false, true]) {
    final name = phone ? 'phone-saved' : 'tablet-saved';
    testWidgets('$name approved reference geometry and real-font capture', (
      tester,
    ) async {
      await _pump(tester, phone: phone);
      expect(tester.takeException(), isNull);
      final actualBounds = <String, Object>{};
      for (final entry in (bounds[name] as Map).entries) {
        final rect = tester.getRect(find.byKey(Key(entry.key as String)));
        final actual = {
          'x': rect.left,
          'y': rect.top,
          'width': rect.width,
          'height': rect.height,
        };
        actualBounds[entry.key as String] = actual;
        for (final field in (entry.value as Map).entries) {
          expect(
            actual[field.key]!,
            closeTo((field.value as num).toDouble(), 1),
            reason: '$name ${entry.key}.${field.key}',
          );
        }
      }
      await tester.runAsync(() async {
        final image = await tester
            .renderObject<RenderRepaintBoundary>(find.byKey(_boundary))
            .toImage(pixelRatio: phone ? 3 : 2);
        final bytes = (await image.toByteData(format: ui.ImageByteFormat.png))!
            .buffer
            .asUint8List();
        await File('test/visual/comparisons/2026-10-19/$name.actual.png')
            .writeAsBytes(bytes);
        await File(
          'test/visual/comparisons/2026-10-19/$name.actual-bounds.json',
        ).writeAsString(
          const JsonEncoder.withIndent('  ').convert(actualBounds),
        );
        image.dispose();
        await expectLater(bytes, matchesGoldenFile('goldens/$name.png'));
      });
    });

    testWidgets('$name stored times, historic semantics and recovery', (
      tester,
    ) async {
      var reads = 0, signIns = 0;
      final semantics = tester.ensureSemantics();
      await _pump(
        tester,
        phone: phone,
        checkedAt: _at.add(const Duration(minutes: 2)),
        refresh: () async {
          reads++;
        },
      );
      await expectLater(tester, meetsGuideline(androidTapTargetGuideline));
      await expectLater(tester, meetsGuideline(labeledTapTargetGuideline));
      _checkContrast();
      expect(
        find.textContaining('downloaded 7:05 AM · last checked 7:07 AM'),
        findsOneWidget,
      );
      expect(find.byKey(const Key('next-stop')), findsNothing);
      expect(find.textContaining('Running'), findsNothing);
      expect(find.text('Report a problem'), findsNothing);
      expect(
        find.bySemanticsLabel(RegExp('Saved estimate .*not live')),
        findsNWidgets(3),
      );
      if (phone) {
        expect(find.textContaining('Planned platform 2'), findsOneWidget);
      }
      await tester.ensureVisible(find.byKey(const Key('saved-reconnect')));
      await tester.tap(find.byKey(const Key('saved-reconnect')));
      await tester.pumpAndSettle();
      expect(reads, 1);
      await _pump(
        tester,
        phone: phone,
        reconnect: () {
          signIns++;
        },
      );
      expect(find.text('Retry connection'), findsNothing);
      await tester.ensureVisible(find.byKey(const Key('saved-reconnect')));
      await tester.tap(find.text('Reconnect and sign in'));
      await tester.pumpAndSettle();
      expect(signIns, 1);
      expect(reads, 1);
      semantics.dispose();
    });

    testWidgets('$name reflows at 200 percent with recovery reachable', (
      tester,
    ) async {
      await _pump(tester, phone: phone, scale: 2, reconnect: () {});
      expect(tester.takeException(), isNull);
      await tester.ensureVisible(find.byKey(const Key('saved-reconnect')));
      expect(find.text('Reconnect and sign in').hitTestable(), findsOneWidget);
      await tester.drag(
        find.byKey(const Key('driver-briefing')),
        const Offset(0, -2400),
      );
      await tester.pumpAndSettle();
      expect(tester.takeException(), isNull);
      expect(find.text('Urgent? Use the radio.').hitTestable(), findsOneWidget);
    });
  }
  testWidgets(
    'an invalid saved segment retains every stop without inventing a next stop',
    (tester) async {
      final raw = jsonDecode(jsonEncode(_fixture)) as Map<String, dynamic>;
      raw['currentPosition'] = null;
      await _pump(tester, raw: raw);
      expect(find.byKey(const Key('stop-veyangoda')), findsOneWidget);
      expect(find.byKey(const Key('next-stop')), findsNothing);
      expect(find.text('Saved delay: 17 min'), findsOneWidget);
    },
  );
}
