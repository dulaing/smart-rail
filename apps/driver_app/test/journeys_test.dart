import 'dart:convert';

import 'package:driver_app/auth/auth_service.dart';
import 'package:driver_app/journeys/briefing_api.dart';
import 'package:driver_app/journeys/briefing_cache.dart';
import 'package:driver_app/journeys/journey_screens.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:smartrail_core/smartrail_core.dart';

class _Auth implements AuthService {
  @override
  Future<String?> ownerId() async => null;
  @override
  Future<String> accessToken() async => 'driver-token';
  @override
  Future<bool> restore() async => true;
  @override
  Future<void> signIn() async {}
  @override
  Future<void> signOut() async {}
}

/// A signed-in owner, as a restored session would report it.
class _OwnedAuth extends _Auth {
  @override
  Future<String?> ownerId() async => 'owner-1';
}

const _journeyId = '11111111-1111-4111-8111-111111111111';
const _stopId = '22222222-2222-4222-8222-222222222222';

String _journeys({bool includeJourney = true}) => jsonEncode({
  'serviceDate': '2026-10-18',
  'journeys': includeJourney
      ? [
          {
            'journeyId': _journeyId,
            'serviceDate': '2026-10-18',
            'trainNumber': 'SIM-1001',
            'timetableLabel': 'Morning express',
            'originName': 'Gampaha',
            'scheduledDeparture': '2026-10-18T02:00:00Z',
            'destinationName': 'Colombo Fort',
            'scheduledArrival': '2026-10-18T03:00:00Z',
            'availability': 'ENDS_EARLY',
          },
        ]
      : [],
});

String _briefing({required DateTime observedAt}) => jsonEncode({
  'journeyId': _journeyId,
  'serviceDate': '2026-10-18',
  'trainNumber': 'SIM-1001',
  'timetable': {
    'kind': 'SIMULATION',
    'label': 'Morning express',
    'checkedAt': null,
  },
  'runningLabel': 'CURRENT',
  'thresholds': {'ageingAfterSeconds': 180, 'unknownAfterSeconds': 240},
  'freshness': 'CURRENT',
  'lastObservation': {
    'observedAt': observedAt.toUtc().toIso8601String(),
    'lastKnownStationId': null,
  },
  'currentPosition': null,
  'delaySeconds': 240,
  'availability': 'ENDS_EARLY',
  'confirmedDelayMinutes': 4,
  'finalStopId': _stopId,
  'journeyVersion': 3,
  'stops': [
    {
      'stopId': _stopId,
      'stationId': '33333333-3333-4333-8333-333333333333',
      'stationName': 'Gampaha',
      'sequence': 1,
      'served': true,
      'scheduledArrival': null,
      'scheduledDeparture': '2026-10-18T02:00:00Z',
      'estimatedArrival': null,
      'estimatedDeparture': '2026-10-18T02:04:00Z',
      'plannedPlatform': '2',
      'confirmedPlatform': '3',
      'confirmedPlatformAt': '2026-10-18T01:50:00Z',
    },
    {
      'stopId': '44444444-4444-4444-8444-444444444444',
      'stationId': '55555555-5555-4555-8555-555555555555',
      'stationName': 'Colombo Fort',
      'sequence': 2,
      'served': false,
      'scheduledArrival': '2026-10-18T03:00:00Z',
      'scheduledDeparture': null,
      'estimatedArrival': null,
      'estimatedDeparture': null,
      'plannedPlatform': null,
      'confirmedPlatform': null,
      'confirmedPlatformAt': null,
    },
  ],
  'changes': [
    {
      'changeId': '66666666-6666-4666-8666-666666666666',
      'kind': 'EARLY_END',
      'confirmedAt': '2026-10-18T01:45:00Z',
      'confirmedByName': 'Control desk',
      'delayMinutes': null,
      'newPlatform': null,
      'targetStopId': null,
      'finalStopId': _stopId,
    },
  ],
});

Widget _app(DriverBriefingApi api) => MaterialApp(
  theme: srTheme(SrProfile.driver, Brightness.light),
  home: DriverJourneysScreen(api: api),
);

DriverBriefingApi _api(Future<http.Response> Function(http.Request) handler) =>
    DriverBriefingApi('https://api.test', _Auth(), client: MockClient(handler));

/// A saved-briefing store that already holds [rows], without touching device storage.
class _FakeCache extends BriefingCache {
  _FakeCache(this.rows);

  final List<Map<String, dynamic>> rows;
  final List<Set<String>> prunedWith = [];

  @override
  Future<List<Map<String, dynamic>>> readAll(String ownerId) async => rows;

  @override
  Future<void> removeUnlisted(
    String ownerId,
    DateTime serviceDate,
    Set<String> listedIds,
  ) async => prunedWith.add(listedIds);
}

void main() {
  test('a saved journey the server no longer lists is dropped, other days are kept', () {
    final rows = [
      {'journeyId': 'a', 'serviceDate': '2026-10-19'},
      {'journeyId': 'b', 'serviceDate': '2026-10-19'},
      {'journeyId': 'c', 'serviceDate': '2026-10-20'},
    ];
    final kept = BriefingCache.withoutUnlisted(rows, '2026-10-19', {'a'});
    expect(kept.map((r) => r['journeyId']), ['a', 'c']);
    // Nothing listed for the day means every saved briefing of that day goes.
    expect(
      BriefingCache.withoutUnlisted(rows, '2026-10-19', {}).map((r) => r['journeyId']),
      ['c'],
    );
  });

  testWidgets(
    'an online list that omits a saved journey tells the cache to drop it',
    (tester) async {
      final cache = _FakeCache([]);
      final api = DriverBriefingApi(
        'https://api.test',
        _OwnedAuth(),
        client: MockClient((_) async => http.Response(_journeys(), 200)),
      );
      await tester.pumpWidget(
        MaterialApp(
          theme: srTheme(SrProfile.driver, Brightness.light),
          home: DriverJourneysScreen(api: api, cache: cache),
        ),
      );
      await tester.pumpAndSettle();

      // The server listed exactly one journey, so only that one may stay saved.
      expect(cache.prunedWith, [
        {_journeyId},
      ]);
    },
  );

  testWidgets(
    'offline, a date with no saved copy says so instead of claiming nothing is assigned',
    (tester) async {
      // The only saved briefing is for a different day than the one shown (today).
      final cache = _FakeCache([
        {
          'journeyId': _journeyId,
          'serviceDate': '2020-01-01',
          'trainNumber': 'SIM-1001',
          'timetableLabel': 'Morning express',
          'originName': 'Gampaha',
          'scheduledDeparture': '2020-01-01T02:00:00Z',
          'destinationName': 'Colombo Fort',
          'scheduledArrival': '2020-01-01T03:00:00Z',
          'availability': 'AS_SCHEDULED',
        },
      ]);
      // No network at all: a call would fail the test.
      final api = _api((_) async => throw StateError('offline'));
      await tester.pumpWidget(
        MaterialApp(
          theme: srTheme(SrProfile.driver, Brightness.light),
          home: DriverJourneysScreen(
            api: api,
            offlineOwnerId: 'owner-1',
            cache: cache,
          ),
        ),
      );
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('no-saved-journeys')), findsOneWidget);
      expect(find.byKey(const Key('no-assigned-journeys')), findsNothing);
      expect(find.textContaining('Reconnect'), findsOneWidget);
    },
  );

  testWidgets('shows an explicit empty assignment state', (tester) async {
    final api = _api(
      (_) async => http.Response(_journeys(includeJourney: false), 200),
    );
    await tester.pumpWidget(_app(api));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('no-assigned-journeys')), findsOneWidget);
  });

  testWidgets(
    'shows a recoverable connection failure without inventing journeys',
    (tester) async {
      final api = _api((_) async => http.Response('unavailable', 503));
      await tester.pumpWidget(_app(api));
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('journey-list-error')), findsOneWidget);
      expect(
        find.text('No assigned journeys for this service date.'),
        findsNothing,
      );
    },
  );

  testWidgets(
    'opens the assigned briefing with truthful early endpoint, platform, issuer and stop list',
    (tester) async {
      final api = _api((request) async {
        if (request.url.path.endsWith('/briefing')) {
          return http.Response(
            _briefing(observedAt: DateTime.now().toUtc()),
            200,
            headers: {
              'etag': '"briefing-v1"',
              'x-smartrail-server-time': DateTime.now()
                  .toUtc()
                  .toIso8601String(),
            },
          );
        }
        return http.Response(_journeys(), 200, headers: {'etag': '"list-v1"'});
      });
      await tester.pumpWidget(_app(api));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('journey-$_journeyId')));
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('driver-briefing')), findsOneWidget);
      expect(find.textContaining('Confirmed platform 3'), findsOneWidget);
      expect(find.text('Not served on this journey'), findsOneWidget);
      expect(
        find.textContaining('ends before its scheduled destination'),
        findsOneWidget,
      );
      await tester.scrollUntilVisible(
        find.textContaining('Control desk'),
        200,
        scrollable: find.byType(Scrollable).last,
      );
      expect(find.textContaining('Control desk'), findsOneWidget);
    },
  );

  testWidgets('removes an old expected time once evidence reaches unknown', (
    tester,
  ) async {
    final api = _api((request) async {
      if (request.url.path.endsWith('/briefing')) {
        return http.Response(
          _briefing(
            observedAt: DateTime.now().toUtc().subtract(
              const Duration(minutes: 5),
            ),
          ),
          200,
        );
      }
      return http.Response(_journeys(), 200);
    });
    await tester.pumpWidget(_app(api));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('journey-$_journeyId')));
    await tester.pumpAndSettle();
    expect(find.textContaining('Expected 02:04'), findsNothing);
    expect(find.textContaining('Position unknown'), findsOneWidget);
    expect(find.textContaining('Confirmed platform 3'), findsOneWidget);
  });
}
