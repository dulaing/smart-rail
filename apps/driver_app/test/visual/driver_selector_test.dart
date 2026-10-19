import 'dart:convert';
import 'dart:io';
import 'dart:ui' as ui;

import 'package:driver_app/journeys/briefing_api.dart';
import 'package:driver_app/journeys/driver_rows.dart';
import 'package:driver_app/journeys/journey_screens.dart';
import 'package:driver_app/journeys/report_api.dart';
import 'package:driver_app/journeys/reports_view.dart';
import 'package:driver_app/journeys/selector_view.dart';
import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:smartrail_core/smartrail_core.dart';

const _boundary = Key('selector-capture');
final _date = DateTime.utc(2026, 10, 12);
// Colombo is UTC+5:30: 01:38Z is 7:08, 00:50Z is 6:20, 01:00Z is 6:30.
final _seven08 = DateTime.utc(2026, 10, 12, 1, 38);
final _six20 = DateTime.utc(2026, 10, 12, 0, 50);
final _six30 = DateTime.utc(2026, 10, 12, 1);
final _received = DateTime.utc(2026, 10, 12, 1, 21);

DriverJourney _journey(
  String id,
  String label,
  String from,
  String to,
  String dep,
  String arr,
  String availability,
) => DriverJourney.fromJson({
  'journeyId': id,
  'serviceDate': '2026-10-12',
  'trainNumber': id,
  'timetableLabel': label,
  'originName': from,
  'scheduledDeparture': dep,
  'destinationName': to,
  'scheduledArrival': arr,
  'availability': availability,
});

final _b1 = _journey(
  'DS-B1',
  'Morning service',
  'Veyangoda',
  'Colombo Fort',
  '2026-10-12T01:22:00Z',
  '2026-10-12T02:31:00Z',
  'AS_SCHEDULED',
);
final _b2 = _journey(
  'DS-B2',
  'Evening service',
  'Colombo Fort',
  'Veyangoda',
  '2026-10-12T12:10:00Z',
  '2026-10-12T13:20:00Z',
  'CANCELLED',
);
// The chooser reference lists the evening service as scheduled.
final _b2Scheduled = _journey(
  'DS-B2',
  'Evening service',
  'Colombo Fort',
  'Veyangoda',
  '2026-10-12T12:10:00Z',
  '2026-10-12T13:20:00Z',
  'AS_SCHEDULED',
);

DriverReport _report() => DriverReport(
  id: 'RPT-118',
  clientReportId: 'client-118',
  journeyId: 'DS-B1',
  reasonCode: 'SIGNAL_DELAY',
  receivedAt: _received,
  state: 'ACKNOWLEDGED',
  acknowledgedByName: 'Dilani',
  acknowledgedAt: _received.add(const Duration(minutes: 4)),
);

Widget _selector({
  String destination = 'journeys',
  SelectorEmpty empty = SelectorEmpty.none,
  bool saved = false,
  bool chooser = false,
  DateTime? now,
  List<SelectorItem>? items,
  ValueChanged<String>? onSelect,
  VoidCallback? onPrevious,
  VoidCallback? onNext,
  VoidCallback? onCheckAgain,
  String? error,
  VoidCallback? onRetry,
}) => DriverSelectorView(
  destination: destination,
  displayName: 'Sunil',
  date: _date,
  saved: saved,
  empty: empty,
  now: now ?? _seven08,
  checkedAt: saved ? null : (now ?? _seven08),
  error: error,
  onRetry: onRetry,
  pending: chooser
      ? const [
          SelectorItem(
            id: 'DS-B1',
            rowKey: Key('pending-DS-B1'),
            title: 'DS-B1 · 12 October 2026',
            copy: 'Train fault · Saved on this device',
            chip: 'RECEIPT NOT CONFIRMED',
            tone: RowTone.danger,
            icon: 'reports',
            semantics: 'DS-B1. Receipt not confirmed.',
          ),
        ]
      : const [],
  items:
      items ??
      (chooser
          ? [journeyItem(_b2Scheduled, saved: false)]
          : [journeyItem(_b1, saved: false), journeyItem(_b2, saved: false)]),
  onPreviousDate: onPrevious ?? () {},
  onNextDate: onNext ?? () {},
  onSelect: onSelect ?? (_) {},
  onCheckAgain: onCheckAgain ?? () {},
);

Widget _list({bool empty = false}) => DriverReportsListView(
  journey: _b1,
  reports: empty ? const [] : [_report()],
  draftReason: empty ? null : 'TRAIN_FAULT',
  now: _seven08,
  checkedAt: _seven08,
  displayName: 'Sunil',
  onSelect: (_) {},
  onReport: () {},
  onOpenReport: (_) {},
  onOpenDraft: () {},
  onBack: () {},
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

  final bounds = {
    ...jsonDecode(
      File(
        'test/visual/comparisons/2026-10-19/selector-reports.reference-bounds.json',
      ).readAsStringSync(),
    ) as Map,
    ...jsonDecode(
      File(
        'test/visual/comparisons/2026-10-19/phone-navigation.reference-bounds.json',
      ).readAsStringSync(),
    ) as Map,
  };

  final captures = <String, (Widget Function(), bool)>{
    'tablet-selector': (_selector, false),
    'tablet-updates-selector': (() => _selector(destination: 'updates'), false),
    'tablet-reports-chooser': (
      () => _selector(destination: 'reports', chooser: true),
      false,
    ),
    'tablet-no-duty': (
      () => _selector(empty: SelectorEmpty.noAssignment, now: _six20),
      false,
    ),
    'tablet-no-saved': (
      () => _selector(empty: SelectorEmpty.noSaved, saved: true, now: _six30),
      false,
    ),
    'phone-selector': (_selector, true),
    'phone-updates-selector': (() => _selector(destination: 'updates'), true),
    'phone-reports-chooser': (
      () => _selector(destination: 'reports', chooser: true),
      true,
    ),
    'phone-no-duty': (
      () => _selector(empty: SelectorEmpty.noAssignment, now: _six20),
      true,
    ),
    'phone-reports-list': (_list, true),
    'phone-reports-empty': (() => _list(empty: true), true),
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
        final bytes = (await image.toByteData(format: ui.ImageByteFormat.png))!
            .buffer
            .asUint8List();
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

  testWidgets(
    'date buttons move the service date and rows open their journey',
    (tester) async {
      final calls = <String>[];
      await _pump(
        tester,
        _selector(
          onPrevious: () => calls.add('previous'),
          onNext: () => calls.add('next'),
          items: [
            journeyItem(_b1, saved: false, onTap: () => calls.add('open')),
          ],
        ),
      );
      await tester.tap(find.byKey(const Key('date-previous')));
      await tester.tap(find.byKey(const Key('date-next')));
      await tester.tap(find.byKey(const Key('journey-DS-B1')));
      expect(calls, ['previous', 'next', 'open']);
      expect(find.text('Mon 12 Oct'), findsWidgets);
    },
  );

  testWidgets('availability and saved copies are labelled, never inferred', (
    tester,
  ) async {
    await _pump(
      tester,
      _selector(
        saved: true,
        items: [
          journeyItem(_b1, saved: true),
          journeyItem(
            _journey(
              'DS-B3',
              'Late service',
              'Gampaha',
              'Colombo Fort',
              '2026-10-12T14:00:00Z',
              '2026-10-12T15:00:00Z',
              'ENDS_EARLY',
            ),
            saved: true,
          ),
          journeyItem(_b2, saved: true),
        ],
      ),
    );
    expect(find.text('AS SCHEDULED'), findsOneWidget);
    expect(find.text('ENDS EARLY'), findsOneWidget);
    expect(find.text('CANCELLED'), findsOneWidget);
    expect(find.text('Saved copy · read only'), findsNWidgets(3));
    expect(find.text('Offline'), findsOneWidget);
  });

  testWidgets('an offline empty date never claims nothing is assigned', (
    tester,
  ) async {
    await _pump(tester, _selector(empty: SelectorEmpty.noSaved, saved: true));
    expect(find.text('No saved briefing available'), findsOneWidget);
    expect(find.text('No duty assignment'), findsNothing);
    expect(find.text('Retry connection'), findsOneWidget);
  });

  testWidgets('a failed read offers retry, a denied one does not', (
    tester,
  ) async {
    var retries = 0;
    await _pump(
      tester,
      _selector(
        items: const [],
        error: 'Could not reach SmartRail.',
        onRetry: () => retries++,
      ),
    );
    await tester.tap(find.text('Try again'));
    expect(retries, 1);
    await _pump(
      tester,
      _selector(
        items: const [],
        error: 'Your access to this journey list has changed.',
      ),
    );
    expect(find.text('Try again'), findsNothing);
    expect(find.byKey(const Key('journey-list-error')), findsOneWidget);
  });

  testWidgets(
    'phone reports list keeps a draft first and its action disabled without a callback',
    (tester) async {
      await _pump(
        tester,
        DriverReportsListView(
          journey: _b1,
          reports: [_report()],
          draftReason: 'TRAIN_FAULT',
          now: _seven08,
          checkedAt: _seven08,
          onSelect: (_) {},
          onReport: null,
        ),
        phone: true,
      );
      expect(
        tester.getTopLeft(find.byKey(const Key('report-row-draft'))).dy,
        lessThan(
          tester.getTopLeft(find.byKey(const Key('report-row-RPT-118'))).dy,
        ),
      );
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
    },
  );
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
  expect(checked, greaterThan(6));
}
