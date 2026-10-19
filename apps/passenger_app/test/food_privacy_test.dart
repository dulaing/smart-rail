import 'dart:convert';

import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:passenger_app/food/order_models.dart';
import 'package:passenger_app/food/request_store.dart';
import 'package:passenger_app/trains/train_cache.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'food_support.dart';
import 'order_support.dart';

void main() {
  setUp(() {
    SharedPreferences.setMockInitialValues({});
    FlutterSecureStorage.setMockInitialValues({});
  });

  test('a full order flow never writes a pickup code or a request into the public preferences', () async {
    final prefs = await SharedPreferences.getInstance();
    await SharedPreferencesTrainCache().saveTimetable([
      journey('today-3001', 'SIM-3001', DateTime(2026, 10, 20)),
    ], DateTime.utc(2026, 10, 20));
    final publicBefore = {for (final k in prefs.getKeys()) k: prefs.get(k)};

    final account = TestAccount();
    final api = FakeOrderSource();
    final orders = ordersFor(account, api, store: SecureFoodRequestStore());
    await account.signInAs('acc-1');
    api.placeScript.add(() async => reply(202, orderJson(status: 'PLACING')));
    await orders.submit(sampleDraft());
    api.readScript.add(() async => reply(200, orderJson()));
    await orders.recover();
    expect(orders.order!.pickupCode, '482915');

    final publicAfter = {for (final k in prefs.getKeys()) k: prefs.get(k)};
    expect(publicAfter, publicBefore, reason: 'the public timetable cache is untouched by ordering');
    final everything = jsonEncode(publicAfter);
    expect(everything.contains('482915'), isFalse);
    expect(everything.contains('clientRequestKey'), isFalse);
    orders.dispose();
  });

  test('the secure request store round-trips per account, isolates accounts and holds no code', () async {
    final store = SecureFoodRequestStore();
    final request = PendingFoodRequest(
      clientRequestKey: 'key-1',
      order: sampleDraft().order,
      display: sampleDraft().display,
      orderId: 'order-1',
      lostAttempts: 2,
    );
    await store.write('acc-1', request);

    final back = (await store.read('acc-1'))!;
    expect((back.clientRequestKey, back.orderId, back.lostAttempts), ('key-1', 'order-1', 2));
    expect(back.order.toRequestJson('key-1'), request.order.toRequestJson('key-1'));
    expect(await store.read('acc-2'), isNull, reason: 'another account sees nothing');

    expect(request.encode().toLowerCase().contains('pickupcode'), isFalse);
    await store.clear('acc-1');
    expect(await store.read('acc-1'), isNull);
  });

  test('an unreadable stored request is treated as none rather than crashing', () async {
    FlutterSecureStorage.setMockInitialValues({'food.request.acc-1': 'not json'});
    expect(await SecureFoodRequestStore().read('acc-1'), isNull);
  });
}
