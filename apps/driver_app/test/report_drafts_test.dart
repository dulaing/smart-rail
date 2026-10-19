import 'dart:convert';

import 'package:driver_app/auth/auth_service.dart';
import 'package:driver_app/journeys/briefing_api.dart';
import 'package:driver_app/journeys/report_api.dart';
import 'package:driver_app/journeys/report_drafts.dart';
import 'package:driver_app/journeys/report_screens.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:smartrail_core/smartrail_core.dart';

class _Auth implements AuthService {
  _Auth(this.owner);
  final String? owner;
  @override
  Future<String?> ownerId() async => owner;
  @override
  Future<String> accessToken() async => 'driver-token';
  @override
  Future<bool> restore() async => true;
  @override
  Future<void> signIn() async {}
  @override
  Future<void> signOut() async {}
}

/// Drafts held in memory but written and read through the real row format.
class _MemoryDrafts extends ReportDraftStore {
  _MemoryDrafts([Map<String, dynamic>? rows]) : rows = rows ?? {};
  final Map<String, dynamic> rows;

  @override
  Future<ReportDraft?> read(String ownerId, String journeyId) async =>
      ReportDraft.fromJson(rows[journeyId], ownerId: ownerId);

  @override
  Future<List<ReportDraft>> pendingFor(String ownerId) async =>
      pendingDrafts(rows, ownerId);

  @override
  Future<ReportDraft> create(
    String ownerId,
    DriverJourney journey,
    String reasonCode,
  ) async {
    final draft = ReportDraft(
      ownerId: ownerId,
      journeyId: journey.id,
      clientReportId: '99999999-9999-4999-8999-999999999999',
      reasonCode: reasonCode,
      journey: journey,
    );
    rows[journey.id] = draft.toJson();
    return draft;
  }

  @override
  Future<void> clear(String journeyId) async => rows.remove(journeyId);

  @override
  Future<void> clearAll() async => rows.clear();
}

const _yesterdayJourney = '11111111-1111-4111-8111-111111111111';
const _todayJourney = '22222222-2222-4222-8222-222222222222';
const _clientId = '33333333-3333-4333-8333-333333333333';

Map<String, dynamic> _journeyJson(String id, String date, String train) => {
  'journeyId': id,
  'serviceDate': date,
  'trainNumber': train,
  'timetableLabel': 'Morning express',
  'originName': 'Gampaha',
  'scheduledDeparture': '${date}T02:00:00Z',
  'destinationName': 'Colombo Fort',
  'scheduledArrival': '${date}T03:00:00Z',
  'availability': 'AS_SCHEDULED',
};

DriverJourney _yesterday() => DriverJourney.fromJson(
  _journeyJson(_yesterdayJourney, '2026-10-18', 'SIM-1001'),
);

/// A report written at 23:58 on 18 October that was never confirmed.
Map<String, dynamic> _lateDraftRows({String owner = 'owner-1'}) => {
  _yesterdayJourney: ReportDraft(
    ownerId: owner,
    journeyId: _yesterdayJourney,
    clientReportId: _clientId,
    reasonCode: 'SIGNAL_DELAY',
    journey: _yesterday(),
  ).toJson(),
};

Map<String, dynamic> _report() => {
  'reportId': '44444444-4444-4444-8444-444444444444',
  'clientReportId': _clientId,
  'journeyId': _yesterdayJourney,
  'reasonCode': 'SIGNAL_DELAY',
  'receivedAt': '2026-10-18T18:50:00Z',
  'state': 'SENT_AWAITING_CONTROL',
  'acknowledgedByName': null,
  'acknowledgedAt': null,
};

/// 00:15 on 19 October in Colombo (UTC+5:30), after the service date rolled over.
DateTime _afterMidnight() => DateTime.utc(2026, 10, 18, 18, 45);

class _Rig {
  _Rig({
    required this.drafts,
    String? owner = 'owner-1',
    int todayStatus = 200,
    this.submitStatus = 201,
    this.loseSubmitResponse = false,
    this.receiptFound = false,
    this.lookupStatus = 200,
  }) {
    final journeyApi = DriverBriefingApi(
      'https://api.test',
      _Auth(owner),
      client: MockClient((request) async {
        journeyRequests.add(request);
        return todayStatus == 200
            ? http.Response(
                jsonEncode({
                  'serviceDate': '2026-10-19',
                  'journeys': [
                    _journeyJson(_todayJourney, '2026-10-19', 'SIM-2002'),
                  ],
                }),
                200,
              )
            : http.Response('{}', todayStatus);
      }),
    );
    final reportApi = DriverReportApi(
      'https://api.test',
      _Auth(owner),
      client: MockClient((request) async {
        reportRequests.add(request);
        if (request.method == 'POST') {
          if (loseSubmitResponse) throw http.ClientException('Response lost');
          return http.Response(jsonEncode(_report()), submitStatus);
        }
        if (request.url.queryParameters.containsKey('clientReportId')) {
          if (lookupStatus != 200) {
            return http.Response('{"detail":"Access ended"}', lookupStatus);
          }
          return http.Response(
            jsonEncode({
              'items': receiptFound ? [_report()] : [],
            }),
            200,
          );
        }
        if (acknowledged) {
          return http.Response(
            jsonEncode({
              'items': [
                {
                  ..._report(),
                  'state': 'ACKNOWLEDGED',
                  'acknowledgedByName': 'Dilani',
                  'acknowledgedAt': '2026-10-18T18:55:00Z',
                },
              ],
            }),
            200,
          );
        }
        return http.Response(
          jsonEncode({'items': <Object>[], 'nextCursor': null}),
          200,
        );
      }),
    );
    this.journeyApi = journeyApi;
    this.reportApi = reportApi;
  }

  final _MemoryDrafts drafts;
  final int submitStatus;
  final bool loseSubmitResponse;
  bool receiptFound;
  final int lookupStatus;
  bool acknowledged = false;
  final journeyRequests = <http.Request>[];
  final reportRequests = <http.Request>[];
  late final DriverBriefingApi journeyApi;
  late final DriverReportApi reportApi;

  Widget app() => MaterialApp(
    theme: srTheme(SrProfile.driver, Brightness.light),
    home: Scaffold(
      body: SingleChildScrollView(
        child: DriverReportsScreen(
          journeyApi: journeyApi,
          reportApi: reportApi,
          drafts: drafts,
          now: _afterMidnight,
        ),
      ),
    ),
  );

  List<http.Request> get posts =>
      reportRequests.where((r) => r.method == 'POST').toList();
}

void main() {
  test('a draft keeps its journey, so it can be listed on any later date', () {
    final draft = ReportDraft.fromJson(
      ReportDraft(
        ownerId: 'owner-1',
        journeyId: _yesterdayJourney,
        clientReportId: _clientId,
        reasonCode: 'SIGNAL_DELAY',
        journey: _yesterday(),
      ).toJson(),
    )!;
    expect(draft.clientReportId, _clientId);
    expect(draft.journey!.trainNumber, 'SIM-1001');
    expect(draft.journey!.serviceDate, DateTime(2026, 10, 18));
    expect(draft.journey!.origin, 'Gampaha');
  });

  test('pending drafts are this owner\'s only, oldest date first, skipping damaged rows', () {
    final older = ReportDraft(
      ownerId: 'owner-1',
      journeyId: 'a',
      clientReportId: 'k1',
      reasonCode: 'TRAIN_FAULT',
      journey: DriverJourney.fromJson(
        _journeyJson('a', '2026-10-17', 'SIM-0001'),
      ),
    ).toJson();
    final rows = {
      'b': _lateDraftRows()[_yesterdayJourney],
      'a': older,
      'c': _lateDraftRows(owner: 'someone-else')[_yesterdayJourney],
      'd': {'broken': true},
      'e': {
        // Saved by an older version of the app: no journey details.
        'ownerId': 'owner-1',
        'journeyId': 'e',
        'clientReportId': 'k5',
        'reasonCode': 'OBSTRUCTION',
      },
    };
    final pending = pendingDrafts(rows, 'owner-1');
    expect(pending.map((d) => d.journeyId), ['e', 'a', _yesterdayJourney]);
    expect(pending.first.journey, isNull);
  });

  testWidgets(
    'a report left unconfirmed before midnight is still listed after it',
    (tester) async {
      final rig = _Rig(drafts: _MemoryDrafts(_lateDraftRows()));
      await tester.pumpWidget(rig.app());
      await tester.pumpAndSettle();

      // The list is read for the new service date, which does not contain yesterday's journey.
      expect(
        rig.journeyRequests.single.url.queryParameters['serviceDate'],
        '2026-10-19',
      );
      expect(find.text('Reports not confirmed'), findsOneWidget);
      expect(find.text('SIM-1001 · 2026-10-18'), findsOneWidget);
      expect(find.text('Signal delay · Receipt not confirmed'), findsOneWidget);
      // Today's journey is still offered for a new report.
      expect(find.text('SIM-2002 · Morning express'), findsOneWidget);
      expect(rig.posts, isEmpty, reason: 'listing a draft never sends it');
    },
  );

  testWidgets(
    'retrying after midnight sends the same report key once, and the draft is cleared',
    (tester) async {
      final drafts = _MemoryDrafts(_lateDraftRows());
      final rig = _Rig(drafts: drafts);
      await tester.pumpWidget(rig.app());
      await tester.pumpAndSettle();

      await tester.tap(find.byKey(const Key('pending-$_yesterdayJourney')));
      await tester.pumpAndSettle();
      expect(find.text('Your report'), findsOneWidget);
      expect(find.text('Retry same report'), findsOneWidget);
      expect(rig.posts, isEmpty, reason: 'opening a draft never sends it');

      await tester.tap(find.text('Retry same report'));
      await tester.pumpAndSettle();

      expect(rig.posts, hasLength(1));
      expect(
        rig.posts.single.url.path,
        '/api/driver/journeys/$_yesterdayJourney/reports',
      );
      expect(jsonDecode(rig.posts.single.body), {
        'clientReportId': _clientId,
        'reasonCode': 'SIGNAL_DELAY',
      });
      expect(drafts.rows, isEmpty);
      expect(
        find.textContaining('Server received this report'),
        findsOneWidget,
      );
      expect(find.text('Waiting for Control'), findsOneWidget);

      // Back on the list the confirmed report is no longer pending.
      await tester.pageBack();
      await tester.pumpAndSettle();
      expect(find.text('Reports not confirmed'), findsNothing);
    },
  );

  testWidgets(
    'a failed retry keeps the draft and its key for another manual try',
    (tester) async {
      final drafts = _MemoryDrafts(_lateDraftRows());
      final rig = _Rig(drafts: drafts, submitStatus: 503);
      await tester.pumpWidget(rig.app());
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('pending-$_yesterdayJourney')));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Retry same report'));
      await tester.pumpAndSettle();

      expect(rig.posts, hasLength(1));
      expect(drafts.rows[_yesterdayJourney]['clientReportId'], _clientId);
      expect(find.text('Retry same report'), findsOneWidget);
      await tester.pageBack();
      await tester.pumpAndSettle();
      expect(find.text('SIM-1001 · 2026-10-18'), findsOneWidget);
    },
  );

  testWidgets(
    'lost response keeps a draft until manual receipt check, then polls named acknowledgement',
    (tester) async {
      final drafts = _MemoryDrafts(_lateDraftRows());
      final rig = _Rig(drafts: drafts, loseSubmitResponse: true);
      await tester.pumpWidget(rig.app());
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('pending-$_yesterdayJourney')));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Retry same report'));
      await tester.pumpAndSettle();
      expect(rig.posts, hasLength(1));
      expect(find.text('Receipt not confirmed'), findsOneWidget);
      expect(find.text('Saved on this device'), findsOneWidget);
      expect(find.textContaining('Control has not been told'), findsNothing);
      expect(drafts.rows[_yesterdayJourney]['clientReportId'], _clientId);
      rig.receiptFound = true;
      await tester.ensureVisible(find.text('Check receipt'));
      await tester.tap(find.text('Check receipt'));
      await tester.pumpAndSettle();
      expect(
        rig.posts,
        hasLength(1),
        reason: 'receipt checking must never resend',
      );
      expect(drafts.rows, isEmpty);
      expect(find.text('Saved on this device'), findsNothing);
      expect(find.text('Waiting for Control'), findsOneWidget);
      expect(find.text('Not acknowledged'), findsOneWidget);
      expect(find.textContaining('Acknowledged by'), findsNothing);
      rig.acknowledged = true;
      await tester.pump(const Duration(seconds: 31));
      await tester.pumpAndSettle();
      expect(find.text('Acknowledged by Dilani'), findsOneWidget);
      expect(find.text('Dilani · 12:25 AM'), findsOneWidget);
      expect(rig.posts, hasLength(1));
    },
  );

  testWidgets(
    'opening a recovered receipt removes the persisted draft without sending',
    (tester) async {
      final drafts = _MemoryDrafts(_lateDraftRows());
      final rig = _Rig(drafts: drafts, receiptFound: true);
      await tester.pumpWidget(rig.app());
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('pending-$_yesterdayJourney')));
      await tester.pumpAndSettle();
      expect(drafts.rows, isEmpty);
      expect(rig.posts, isEmpty);
      expect(find.text('Waiting for Control'), findsOneWidget);
      expect(find.text('Saved on this device'), findsNothing);
    },
  );

  testWidgets('denied receipt lookup retains draft and disables retry', (
    tester,
  ) async {
    final drafts = _MemoryDrafts(_lateDraftRows());
    final rig = _Rig(drafts: drafts, lookupStatus: 403);
    await tester.pumpWidget(rig.app());
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('pending-$_yesterdayJourney')));
    await tester.pumpAndSettle();
    expect(
      find.text('Access ended. Sign in again or contact Control.'),
      findsOneWidget,
    );
    await tester.tap(find.text('Retry same report'));
    await tester.pumpAndSettle();
    expect(rig.posts, isEmpty);
    expect(drafts.rows[_yesterdayJourney]['clientReportId'], _clientId);
  });

  testWidgets(
    'the pending report is listed even when today\'s journeys cannot be read',
    (tester) async {
      final rig = _Rig(
        drafts: _MemoryDrafts(_lateDraftRows()),
        todayStatus: 500,
      );
      await tester.pumpWidget(rig.app());
      await tester.pumpAndSettle();
      expect(find.text('SIM-1001 · 2026-10-18'), findsOneWidget);
      expect(
        find.text(
          'Could not load your assigned journeys. Reconnect and try again.',
        ),
        findsOneWidget,
      );
    },
  );

  testWidgets('another driver\'s unconfirmed report is not listed', (
    tester,
  ) async {
    final rig = _Rig(
      drafts: _MemoryDrafts(_lateDraftRows(owner: 'someone-else')),
    );
    await tester.pumpWidget(rig.app());
    await tester.pumpAndSettle();
    expect(find.text('Reports not confirmed'), findsNothing);
    expect(find.text('SIM-1001 · 2026-10-18'), findsNothing);
  });

  testWidgets(
    'a journey with an unconfirmed report is listed once, not also as a new choice',
    (tester) async {
      final todayDraft = {
        _todayJourney: ReportDraft(
          ownerId: 'owner-1',
          journeyId: _todayJourney,
          clientReportId: _clientId,
          reasonCode: 'TRAIN_FAULT',
          journey: DriverJourney.fromJson(
            _journeyJson(_todayJourney, '2026-10-19', 'SIM-2002'),
          ),
        ).toJson(),
      };
      final rig = _Rig(drafts: _MemoryDrafts(todayDraft));
      await tester.pumpWidget(rig.app());
      await tester.pumpAndSettle();
      expect(find.text('SIM-2002 · 2026-10-19'), findsOneWidget);
      expect(find.text('SIM-2002 · Morning express'), findsNothing);
    },
  );

  testWidgets(
    'a draft saved before journeys were kept with it is still reachable',
    (tester) async {
      final legacy = {
        _yesterdayJourney: {
          'ownerId': 'owner-1',
          'journeyId': _yesterdayJourney,
          'clientReportId': _clientId,
          'reasonCode': 'OBSTRUCTION',
        },
      };
      final drafts = _MemoryDrafts(legacy);
      final rig = _Rig(drafts: drafts);
      await tester.pumpWidget(rig.app());
      await tester.pumpAndSettle();
      expect(find.text('Earlier journey'), findsOneWidget);
      await tester.tap(find.byKey(const Key('pending-$_yesterdayJourney')));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Retry same report'));
      await tester.pumpAndSettle();
      expect(jsonDecode(rig.posts.single.body)['clientReportId'], _clientId);
    },
  );
}
