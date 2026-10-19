import 'dart:convert';
import 'dart:io';
import 'dart:ui' as ui;

import 'package:driver_app/gate_view.dart';
import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:smartrail_core/smartrail_core.dart';

const _boundary = Key('gate-capture');

Widget _gate(
  GateKind kind, {
  String? message,
  bool failed = false,
  VoidCallback? onSignIn,
  VoidCallback? onSignOut,
  VoidCallback? onRetry,
  bool busy = false,
}) => DriverGateView(
  kind: kind,
  message: message,
  signInFailed: failed,
  signInFailedMessage: 'Sign-in was cancelled or failed. Try again.',
  busy: busy,
  onSignIn: onSignIn ?? () {},
  onSignOut: onSignOut ?? () {},
  onRetry: onRetry ?? () {},
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

  final bounds = jsonDecode(
    File('test/visual/comparisons/2026-10-19/gate.reference-bounds.json')
        .readAsStringSync(),
  ) as Map;

  final captures = <String, (Widget Function(), bool)>{
    'tablet-signed-out': (() => _gate(GateKind.signedOut), false),
    'tablet-expired': (() => _gate(GateKind.expired), false),
    'phone-signed-out': (() => _gate(GateKind.signedOut), true),
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
  }

  // Every signed-out state, at both sizes, keeps its controls reachable and readable.
  for (final phone in [false, true]) {
    for (final kind in GateKind.values) {
      testWidgets(
        '${phone ? 'phone' : 'tablet'} ${kind.name} targets and 200% text',
        (tester) async {
          final semantics = tester.ensureSemantics();
          Widget build() => _gate(
            kind,
            message: 'Could not reach SmartRail.',
            failed: kind == GateKind.signedOut,
          );
          await _pump(tester, build(), phone: phone);
          await expectLater(tester, meetsGuideline(androidTapTargetGuideline));
          await expectLater(tester, meetsGuideline(labeledTapTargetGuideline));
          _checkContrast();
          semantics.dispose();
          await _pump(tester, build(), phone: phone, scale: 2);
          expect(tester.takeException(), isNull);
          await tester.drag(
            find.byKey(const Key('gate-scroll')),
            const Offset(0, -2400),
          );
          await tester.pumpAndSettle();
          expect(tester.takeException(), isNull);
        },
      );
    }
  }

  testWidgets(
    'signed-out shows no identity before sign-in and one notice on failure',
    (tester) async {
      await _pump(tester, _gate(GateKind.signedOut, failed: true));
      expect(find.textContaining('@'), findsNothing);
      expect(find.textContaining('DEMO STAFF'), findsNothing);
      expect(find.byKey(const Key('notice')), findsOneWidget);
    },
  );

  testWidgets('each signed-out state offers only its own actions', (
    tester,
  ) async {
    final calls = <String>[];
    Future<void> show(GateKind kind, {String? message}) => _pump(
      tester,
      _gate(
        kind,
        message: message,
        onSignIn: () => calls.add('signIn'),
        onSignOut: () => calls.add('signOut'),
        onRetry: () => calls.add('retry'),
      ),
    );
    await show(GateKind.checking);
    expect(find.byType(FilledButton), findsNothing);
    expect(find.text('Checking your session…'), findsOneWidget);

    await show(GateKind.missing, message: 'No SmartRail account is linked.');
    expect(find.text('Try again'), findsNothing);
    await tester.tap(find.text('Sign out'));
    expect(calls, ['signOut']);

    await show(GateKind.failed, message: 'Could not reach SmartRail.');
    expect(find.text('Could not reach SmartRail.'), findsWidgets);
    await tester.tap(find.text('Try again'));
    await tester.tap(find.text('Sign out'));
    expect(calls, ['signOut', 'retry', 'signOut']);

    await show(GateKind.expired);
    await tester.tap(find.text('Sign in again'));
    expect(calls.last, 'signIn');
  });

  testWidgets('a busy gate does not start a second sign-in', (tester) async {
    var signIns = 0;
    await _pump(
      tester,
      _gate(GateKind.signedOut, busy: true, onSignIn: () => signIns++),
    );
    await tester.tap(find.byKey(const Key('gate-primary')));
    expect(signIns, 0);
  });

  testWidgets(
    'phone connection failure keeps manual sign-out; busy actions are disabled',
    (tester) async {
      var retries = 0, signOuts = 0;
      Widget build(bool busy) => _gate(
        GateKind.failed,
        busy: busy,
        onRetry: () => retries++,
        onSignOut: () => signOuts++,
      );
      await _pump(tester, build(false), phone: true);
      await tester.tap(find.byKey(const Key('gate-primary')));
      await tester.tap(find.byKey(const Key('gate-secondary')));
      expect([retries, signOuts], [1, 1]);
      await _pump(tester, build(true), phone: true);
      for (final key in ['gate-primary', 'gate-secondary']) {
        final button = tester.widget<FilledButton>(
          find.descendant(
            of: find.byKey(Key(key)),
            matching: find.byType(FilledButton),
          ),
        );
        expect(button.onPressed, isNull);
      }
    },
  );
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
