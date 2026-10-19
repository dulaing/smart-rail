import 'dart:convert';
import 'dart:io';
import 'dart:ui' as ui;

import 'package:driver_app/account/account_view.dart';
import 'package:driver_app/journeys/briefing_api.dart';
import 'package:driver_app/journeys/report_api.dart';
import 'package:driver_app/journeys/reports_view.dart';
import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:smartrail_core/smartrail_core.dart';

final _journey = DriverJourney.fromJson(
  jsonDecode(File('test/visual/fixtures/duty.json').readAsStringSync())
      as Map<String, dynamic>,
);
const _boundary = Key('account-capture');
// 7:08 in Colombo (UTC+5:30), the clock shown by every reference in this family.
final _clock = DateTime.utc(2026, 10, 12, 1, 38);
final _received = DateTime.utc(2026, 10, 12, 1, 21);

DriverReport _report() => DriverReport(
  id: 'RPT-118',
  clientReportId: 'client-118',
  journeyId: _journey.id,
  reasonCode: 'SIGNAL_DELAY',
  receivedAt: _received,
  state: 'ACKNOWLEDGED',
  acknowledgedByName: 'Dilani',
  acknowledgedAt: _received.add(const Duration(minutes: 4)),
);

Widget _account({
  List<String> roles = const ['DRIVER'],
  String? noAccess,
  bool offline = false,
  bool journeys = true,
  ValueChanged<String>? onSelect,
  VoidCallback? onSignOut,
}) => DriverAccountView(
  displayName: 'Sunil',
  roles: roles,
  noAccessMessage: noAccess,
  offline: offline,
  showJourneys: journeys,
  now: _clock,
  checkedAt: _clock,
  onSignOut: onSignOut ?? () {},
  onSelect: onSelect ?? (_) {},
);

Widget _list({bool empty = false, bool draft = false, bool ack = true}) =>
    DriverReportsListView(
      journey: _journey,
      reports: empty || !ack ? const [] : [_report()],
      draftReason: draft ? 'TRAIN_FAULT' : null,
      now: empty ? _clock.subtract(const Duration(minutes: 38)) : _clock,
      checkedAt: empty ? _clock.subtract(const Duration(minutes: 38)) : _clock,
      displayName: 'Sunil',
      onSelect: (_) {},
      onReport: () {},
      onOpenReport: (_) {},
      onOpenDraft: () {},
    );

Future<void> _pump(
  WidgetTester tester,
  Widget view, {
  bool phone = false,
  double scale = 1,
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
        child: RepaintBoundary(key: _boundary, child: view),
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

  final bounds =
      jsonDecode(
            File(
              'test/visual/comparisons/2026-10-19/account-reports.reference-bounds.json',
            ).readAsStringSync(),
          )
          as Map;

  final captures = <String, (Widget Function(), bool)>{
    'tablet-account': (_account, false),
    'phone-account': (_account, true),
    'tablet-reports-list': (() => _list(draft: true), false),
    'tablet-reports-empty': (() => _list(empty: true), false),
  };

  for (final MapEntry(key: name, value: (build, phone)) in captures.entries) {
    testWidgets('$name real-font reference geometry and capture', (
      tester,
    ) async {
      await _pump(tester, build(), phone: phone);
      expect(tester.takeException(), isNull);
      final geometry = <String, Object>{};
      for (final entry in (bounds[name] as Map).entries) {
        final rect = tester.getRect(find.byKey(Key(entry.key as String)));
        final actual = {
          'x': rect.left,
          'y': rect.top,
          'width': rect.width,
          'height': rect.height,
        };
        geometry[entry.key as String] = actual;
        for (final field in (entry.value as Map).entries) {
          expect(
            actual[field.key]!,
            closeTo((field.value as num).toDouble(), 1),
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

    testWidgets('$name targets, labels, 7:1 contrast and 200% text', (
      tester,
    ) async {
      final semantics = tester.ensureSemantics();
      await _pump(tester, build(), phone: phone);
      await expectLater(tester, meetsGuideline(androidTapTargetGuideline));
      await expectLater(tester, meetsGuideline(labeledTapTargetGuideline));
      _checkContrast();
      semantics.dispose();
      await _pump(tester, build(), phone: phone, scale: 2);
      expect(tester.takeException(), isNull);
    });
  }

  testWidgets('account never shows an email, a staff role or an assignment', (
    tester,
  ) async {
    await _pump(tester, _account(roles: const ['DRIVER', 'SYS_ADMIN']));
    expect(find.textContaining('@'), findsNothing);
    expect(find.textContaining('DS-B1'), findsNothing);
    expect(find.text('Driver, Sys admin'), findsOneWidget);
    expect(find.text('Signed-in staff'), findsOneWidget);
    expect(find.text('Open briefings to choose a journey.'), findsOneWidget);
  });

  testWidgets('account actions follow permissions and connection', (
    tester,
  ) async {
    final selected = <String>[];
    var signOuts = 0;
    await _pump(
      tester,
      _account(onSelect: selected.add, onSignOut: () => signOuts++),
    );
    await tester.tap(find.byKey(const Key('account-open-briefings')));
    await tester.tap(find.byKey(const Key('account-signout')));
    expect(selected, ['journeys']);
    expect(signOuts, 1);

    await _pump(
      tester,
      _account(
        noAccess: 'This account has no driver access.',
        journeys: false,
        offline: true,
      ),
    );
    expect(find.byKey(const Key('account-open-briefings')), findsNothing);
    expect(find.byKey(const Key('tab-journeys')), findsNothing);
    expect(find.text('Reconnect and sign in'), findsOneWidget);
    expect(find.text('Offline'), findsOneWidget);
    expect(find.byKey(const Key('no-driver-access')), findsOneWidget);
  });

  testWidgets('phone account without driver access has no way back', (
    tester,
  ) async {
    await _pump(
      tester,
      _account(noAccess: 'No access.', journeys: false),
      phone: true,
    );
    expect(find.byKey(const Key('account-briefings')), findsNothing);
    expect(find.byTooltip('Back'), findsNothing);
  });

  testWidgets('a draft is listed first and blocks a second report', (
    tester,
  ) async {
    var opened = '';
    await _pump(
      tester,
      DriverReportsListView(
        journey: _journey,
        reports: [_report()],
        draftReason: 'TRAIN_FAULT',
        now: _clock,
        checkedAt: _clock,
        onSelect: (_) {},
        onReport: null,
        onOpenReport: (report) => opened = report.id,
        onOpenDraft: () => opened = 'draft',
      ),
    );
    expect(
      tester.getTopLeft(find.byKey(const Key('report-row-draft'))).dy,
      lessThan(tester.getTopLeft(find.byKey(const Key('report-row-RPT-118'))).dy),
    );
    expect(find.text('RECEIPT NOT CONFIRMED'), findsOneWidget);
    expect(find.text('Saved on this device'), findsOneWidget);
    expect(find.textContaining('RPT-118 ·'), findsNothing);
    expect(find.text('Reports total'), findsNothing);
    expect(
      tester
          .widget<FilledButton>(
            find.descendant(
              of: find.byKey(const Key('reports-primary')),
              matching: find.byType(FilledButton),
            ),
          )
          .onPressed,
      isNull,
    );
    await tester.tap(find.byKey(const Key('report-row-draft')));
    expect(opened, 'draft');
    await tester.tap(find.byKey(const Key('report-row-RPT-118')));
    expect(opened, 'RPT-118');
  });

  testWidgets('an awaiting report names no acknowledger', (tester) async {
    await _pump(
      tester,
      DriverReportsListView(
        journey: _journey,
        reports: [
          DriverReport(
            id: 'RPT-9',
            clientReportId: 'c',
            journeyId: _journey.id,
            reasonCode: 'OBSTRUCTION',
            receivedAt: _received,
            state: 'SENT_AWAITING_CONTROL',
            acknowledgedByName: null,
            acknowledgedAt: null,
          ),
        ],
        now: _clock,
        checkedAt: _clock,
        onSelect: (_) {},
      ),
    );
    expect(find.text('AWAITING CONTROL'), findsOneWidget);
    expect(find.text('Obstruction'), findsOneWidget);
    expect(find.text('Server received 6:51'), findsOneWidget);
    expect(find.textContaining('Dilani'), findsNothing);
  });

  testWidgets('reports list explains a failed read and a lost session', (
    tester,
  ) async {
    await _pump(
      tester,
      DriverReportsListView(
        journey: _journey,
        reports: const [],
        notice: 'Could not reach SmartRail.',
        onSelect: (_) {},
      ),
    );
    expect(find.text('Could not reach SmartRail.'), findsOneWidget);
    expect(find.text('No reports sent yet'), findsNothing);
    await _pump(
      tester,
      DriverReportsListView(
        journey: _journey,
        reports: const [],
        accessLost: true,
        onSelect: (_) {},
      ),
    );
    expect(
      find.text('Access ended. Sign in again or contact Control.'),
      findsOneWidget,
    );
    expect(find.text('Not checked'), findsOneWidget);
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
