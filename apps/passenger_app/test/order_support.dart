import 'dart:convert';

import 'package:passenger_app/account/account_controller.dart';
import 'package:passenger_app/auth/account_api.dart';
import 'package:passenger_app/auth/auth_service.dart';
import 'package:passenger_app/food/order_api.dart';
import 'package:passenger_app/food/order_models.dart';
import 'package:passenger_app/food/orders_controller.dart';
import 'package:passenger_app/food/request_store.dart';

class FoodFakeAuth implements AuthService {
  FoodFakeAuth({this.signInFails = false});
  bool signInFails;
  bool signedIn = false;
  int signOuts = 0;
  bool tokenFails = false;

  @override
  Future<bool> restore() async => false;
  @override
  Future<void> signIn() async {
    if (signInFails) throw StateError('cancelled');
    signedIn = true;
  }

  @override
  Future<String> accessToken() async {
    if (tokenFails) throw StateError('Not signed in.');
    return 'token';
  }

  @override
  Future<void> signOut() async {
    signedIn = false;
    signOuts++;
  }
}

class FoodFakeAccounts implements AccountApi {
  FoodFakeAccounts(this.result);
  AccountResult result;
  @override
  Future<AccountResult> fetchMe(String accessToken) async => result;
}

Account accountNamed(String id, {List<String> permissions = const ['orders.manage']}) =>
    Account(accountId: id, displayName: 'Demo $id', roles: const ['PASSENGER'], permissions: permissions);

/// An account controller that starts as a guest; call [signInAs] to become [id].
class TestAccount {
  TestAccount({FoodFakeAuth? auth, FoodFakeAccounts? accounts})
    : auth = auth ?? FoodFakeAuth(),
      accounts = accounts ?? FoodFakeAccounts(AccountFound(accountNamed('acc-1'))) {
    controller = AccountController(auth: this.auth, accounts: this.accounts);
  }
  final FoodFakeAuth auth;
  final FoodFakeAccounts accounts;
  late final AccountController controller;

  Future<void> signInAs(String id, {List<String> permissions = const ['orders.manage']}) async {
    accounts.result = AccountFound(accountNamed(id, permissions: permissions));
    await controller.signIn();
    await Future<void>.delayed(Duration.zero);
  }

  Future<void> signOut() async {
    await controller.signOut();
    await Future<void>.delayed(Duration.zero);
  }
}

/// Order JSON exactly as OrderDto serializes it.
Map<String, dynamic> orderJson({
  String id = 'order-1',
  String status = 'ACCEPTED',
  int sequence = 1,
  String? pickupCode = '482915',
  String? reasonCode,
  DateTime? estimate,
  DateTime? evidence,
  DateTime? updatedAt,
  String point = 'peradeniya',
}) {
  final open = status != 'PLACING' && status != 'REJECTED';
  return {
    'id': id,
    'status': status,
    'statusSequence': sequence,
    'collectionPointId': point,
    'journeyId': 'today-3001',
    'pickupStopId': 'pdn',
    'pickupCode': open ? pickupCode : null,
    'items': open
        ? [
            {'menuItemId': 'pdn-lamprais', 'name': 'Lamprais', 'quantity': 2, 'unitPriceMinorLkr': 85000},
          ]
        : null,
    'totalMinorLkr': open ? 170000 : null,
    'pickupTime': open
        ? {
            'scheduledArrival': '2026-10-20T03:30:00.000Z',
            'estimatedArrival': (estimate ?? DateTime.utc(2026, 10, 20, 3, 35)).toIso8601String(),
            'etaEvidenceAt': (evidence ?? DateTime.utc(2026, 10, 20, 3, 20)).toIso8601String(),
          }
        : null,
    'reasonCode': reasonCode,
    'createdAt': '2026-10-20T02:31:00.000Z',
    'updatedAt': (updatedAt ?? DateTime.utc(2026, 10, 20, 2, 31)).toIso8601String(),
  };
}

OrderReply reply(int status, [Map<String, dynamic>? json, String? code]) =>
    OrderReply(status: status, json: json, code: code ?? (json?['code'] as String?));

OrderReply problem(int status, String code, {String? orderId}) =>
    OrderReply(status: status, json: {'status': status, 'code': code, 'orderId': ?orderId}, code: code);

typedef Scripted = Future<OrderReply> Function();

/// Scripted order server: each call pops the next scripted reply for its kind, or uses [fallback].
class FakeOrderSource implements FoodOrderSource {
  final List<Scripted> placeScript = [];
  final List<Scripted> readScript = [];
  final List<Scripted> listScript = [];
  final List<Scripted> cancelScript = [];
  Scripted fallback = () async => const OrderReply.lost();

  /// What `place` was called with: the exact JSON body, including the key.
  final List<Map<String, dynamic>> placed = [];
  final List<String> reads = [];
  final List<String> cancels = [];
  int lists = 0;
  void Function(Map<String, dynamic> body)? onPlace;

  Future<OrderReply> _next(List<Scripted> script) => script.isEmpty ? fallback() : script.removeAt(0)();

  @override
  Future<OrderReply> place(String token, CanonicalOrder order, String clientRequestKey) {
    final body = order.toRequestJson(clientRequestKey);
    placed.add(jsonDecode(jsonEncode(body)) as Map<String, dynamic>);
    onPlace?.call(body);
    return _next(placeScript);
  }

  @override
  Future<OrderReply> read(String token, String orderId) {
    reads.add(orderId);
    return _next(readScript);
  }

  @override
  Future<OrderReply> list(String token) {
    lists++;
    return _next(listScript);
  }

  @override
  Future<OrderReply> cancel(String token, String orderId) {
    cancels.add(orderId);
    return _next(cancelScript);
  }
}

/// A draft as FoodController.buildDraft would produce for lamprais at Peradeniya.
OrderDraft sampleDraft({int quantity = 2}) => OrderDraft(
  order: CanonicalOrder(
    collectionPointId: 'peradeniya',
    journeyId: 'today-3001',
    pickupStopId: 'pdn',
    items: [DraftItem('pdn-lamprais', quantity)],
  ),
  display: OrderDisplay(
    pointName: 'Peradeniya Junction',
    trainNumber: 'SIM-3001',
    serviceDate: '2026-10-20',
    lines: [DisplayLine(name: 'Lamprais', quantity: quantity, unitPriceMinorLkr: 85000)],
  ),
);

int _keyCounter = 0;
String testKey() => 'key-${++_keyCounter}';

FoodOrdersController ordersFor(
  TestAccount account,
  FoodOrderSource api, {
  MemoryFoodRequestStore? store,
  String Function()? newKey,
}) => FoodOrdersController(
  account: account.controller,
  api: api,
  store: store ?? MemoryFoodRequestStore(),
  newKey: newKey ?? testKey,
);
