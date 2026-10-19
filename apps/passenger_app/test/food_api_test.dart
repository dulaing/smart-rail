import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:passenger_app/food/food_api.dart';

// Bodies are shaped exactly like services/api Catering/Orders BrowseEndpoints (camelCase JSON).
const _options = {
  'journeyId': '5f0f4c2e-7d9a-4a39-9d6f-2f6d1d5a0001',
  'trainNumber': 'SIM-3001',
  'serviceDate': '2026-10-20',
  'options': [
    {
      'stopId': 'a1',
      'stationId': 's1',
      'collectionPoint': {'id': 'polgahawela-junction', 'name': 'Polgahawela Junction', 'cutoffMinutes': 30},
      'scheduledArrival': '2026-10-20T04:10:00+00:00',
      'estimatedArrival': '2026-10-20T04:15:00+00:00',
      'orderBy': '2026-10-20T03:45:00+00:00',
      'orderable': true,
      'reasonCode': null,
    },
    {
      'stopId': 'a2',
      'stationId': 's2',
      'collectionPoint': {'id': 'peradeniya-junction', 'name': 'Peradeniya Junction', 'cutoffMinutes': 30},
      'scheduledArrival': '2026-10-20T06:00:00+00:00',
      'estimatedArrival': null,
      'orderBy': null,
      'orderable': false,
      'reasonCode': 'TRAIN_UNKNOWN',
    },
  ],
};

const _menu = {
  'collectionPointId': 'peradeniya-junction',
  'currency': 'LKR',
  'items': [
    {'id': 'pdn-lamprais', 'name': 'Lamprais', 'priceMinorLkr': 85000, 'stockQuantity': 25},
  ],
};

http.Response _json(Object body, [int status = 200]) =>
    http.Response(jsonEncode(body), status, headers: {'content-type': 'application/json'});

void main() {
  test('reads collection options with the exact serialized field names', () async {
    late Uri requested;
    final api = FoodApi(
      'https://api.test',
      client: MockClient((request) async {
        requested = request.url;
        expect(request.headers.containsKey('authorization'), isFalse, reason: 'browsing is public');
        return _json(_options);
      }),
    );
    final read = await api.collectionOptions('5f0f4c2e-7d9a-4a39-9d6f-2f6d1d5a0001');
    expect(requested.path, '/api/journeys/5f0f4c2e-7d9a-4a39-9d6f-2f6d1d5a0001/collection-options');
    expect(read.trainNumber, 'SIM-3001');
    final first = read.options.first;
    expect((first.stopId, first.pointId, first.cutoffMinutes, first.orderable), ('a1', 'polgahawela-junction', 30, true));
    expect(first.estimatedArrival, DateTime.utc(2026, 10, 20, 4, 15));
    final second = read.options.last;
    expect((second.orderable, second.reasonCode, second.estimatedArrival, second.orderBy), (false, 'TRAIN_UNKNOWN', null, null));
  });

  test('reads a menu with integer minor LKR prices', () async {
    late Uri requested;
    final api = FoodApi(
      'https://api.test',
      client: MockClient((request) async {
        requested = request.url;
        return _json(_menu);
      }),
    );
    final menu = await api.menu('peradeniya-junction');
    expect(requested.path, '/api/food/collection-points/peradeniya-junction/menu');
    expect(menu.currency, 'LKR');
    expect(menu.items.single.priceMinorLkr, 85000);
  });

  test('maps provider outage, unknown ids and connection loss to distinct failures', () async {
    Future<FoodReadException> failure(http.Response Function() respond) async {
      final api = FoodApi('https://api.test', client: MockClient((_) async => respond()));
      try {
        await api.collectionOptions('x');
      } on FoodReadException catch (e) {
        return e;
      }
      fail('expected a FoodReadException');
    }

    final outage = await failure(
      () => _json({'status': 503, 'code': 'PROVIDER_UNAVAILABLE', 'title': 'Food service unavailable'}, 503),
    );
    expect((outage.failure, outage.code), (FoodReadFailure.providerUnavailable, 'PROVIDER_UNAVAILABLE'));

    final missing = await failure(() => _json({'status': 404, 'code': 'JOURNEY_NOT_FOUND'}, 404));
    expect((missing.failure, missing.code), (FoodReadFailure.notFound, 'JOURNEY_NOT_FOUND'));

    final broken = await failure(() => http.Response('<html>', 502));
    expect(broken.failure, FoodReadFailure.other);

    final lost = FoodApi('https://api.test', client: MockClient((_) async => throw http.ClientException('offline')));
    await expectLater(
      lost.menu('x'),
      throwsA(isA<FoodReadException>().having((e) => e.failure, 'failure', FoodReadFailure.connection)),
    );
  });

  test('problemCode reads the stable code and ignores anything else', () {
    expect(problemCode('{"code":"STOP_PASSED"}'), 'STOP_PASSED');
    expect(problemCode('{"title":"x"}'), isNull);
    expect(problemCode('not json'), isNull);
    expect(problemCode('[]'), isNull);
  });
}
