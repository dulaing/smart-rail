import 'dart:convert';

import 'package:driver_app/auth/auth_service.dart';
import 'package:driver_app/journeys/report_api.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';

class _Auth implements AuthService {
  @override
  Future<bool> restore() async => true;
  @override
  Future<String?> ownerId() async => 'driver-subject';
  @override
  Future<void> signIn() async {}
  @override
  Future<String> accessToken() async => 'driver-token';
  @override
  Future<void> signOut() async {}
}

const _journeyId = '11111111-1111-4111-8111-111111111111';
const _clientId = '22222222-2222-4222-8222-222222222222';
const _reportId = '33333333-3333-4333-8333-333333333333';

Map<String, dynamic> _report({String state = 'SENT_AWAITING_CONTROL'}) => {
  'reportId': _reportId,
  'clientReportId': _clientId,
  'journeyId': _journeyId,
  'reasonCode': 'TRAIN_FAULT',
  'receivedAt': '2026-10-18T10:00:00Z',
  'state': state,
  'acknowledgedByName': state == 'ACKNOWLEDGED' ? 'Control desk' : null,
  'acknowledgedAt': state == 'ACKNOWLEDGED' ? '2026-10-18T10:05:00Z' : null,
};

void main() {
  test(
    'posts the durable key and parses receipt separately from acknowledgement',
    () async {
      final requests = <http.Request>[];
      var calls = 0;
      final api = DriverReportApi(
        'https://api.test',
        _Auth(),
        client: MockClient((request) async {
          requests.add(request);
          calls++;
          return http.Response(jsonEncode(_report()), calls == 1 ? 201 : 200);
        }),
      );

      final first = await api.submit(_journeyId, _clientId, 'TRAIN_FAULT');
      final retry = await api.submit(_journeyId, _clientId, 'TRAIN_FAULT');

      expect(requests.map((request) => request.method), ['POST', 'POST']);
      expect(
        requests.first.url.path,
        '/api/driver/journeys/$_journeyId/reports',
      );
      expect(requests.first.headers['authorization'], 'Bearer driver-token');
      expect(jsonDecode(requests.first.body), {
        'clientReportId': _clientId,
        'reasonCode': 'TRAIN_FAULT',
      });
      expect((first as ReportOk).report.state, 'SENT_AWAITING_CONTROL');
      expect((retry as ReportOk).report.acknowledgedByName, isNull);
    },
  );

  test('looks up the same key and returns the named acknowledgement', () async {
    http.Request? request;
    final api = DriverReportApi(
      'https://api.test',
      _Auth(),
      client: MockClient((value) async {
        request = value;
        return http.Response(
          jsonEncode({
            'items': [_report(state: 'ACKNOWLEDGED')],
            'nextCursor': null,
          }),
          200,
        );
      }),
    );

    final result = await api.findByClientId(_journeyId, _clientId);

    expect(request!.method, 'GET');
    expect(request!.url.queryParameters['clientReportId'], _clientId);
    final report = (result as ReportListOk).reports.single;
    expect(report.state, 'ACKNOWLEDGED');
    expect(report.acknowledgedByName, 'Control desk');
    expect(report.acknowledgedAt, DateTime.parse('2026-10-18T10:05:00Z'));
  });

  test('preserves conflict code for reconciliation instead of generating a new key', () async {
    final api = DriverReportApi(
      'https://api.test',
      _Auth(),
      client: MockClient(
        (_) async => http.Response(
          jsonEncode({
            'title': 'Report key already used',
            'detail': 'Use a new one.',
            'code': 'CLIENT_REPORT_ID_IN_USE',
          }),
          409,
        ),
      ),
    );

    final result = await api.submit(_journeyId, _clientId, 'TRAIN_FAULT');

    expect(result, isA<ReportFailure>());
    expect((result as ReportFailure).code, 'CLIENT_REPORT_ID_IN_USE');
    expect(result.status, 409);
  });
}
