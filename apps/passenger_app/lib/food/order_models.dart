import 'dart:convert';

/// Status strings exactly as `OrderDto.status` serializes them. There is no PREPARING state.
abstract final class OrderStatus {
  static const placing = 'PLACING';
  static const rejected = 'REJECTED';
  static const accepted = 'ACCEPTED';
  static const ready = 'READY';
  static const collected = 'COLLECTED';
  static const noShow = 'NO_SHOW';
  static const cancelled = 'CANCELLED';

  /// Ends the order's life: nothing about it changes any more.
  static bool isTerminal(String status) =>
      status == rejected || status == collected || status == noShow || status == cancelled;

  /// How far along an order is. A response can only move an order forward (or sideways at the
  /// same rank with a newer sequence), never back, so a late reply cannot undo Ready or Collected.
  static int rank(String status) => switch (status) {
    placing => 0,
    accepted => 1,
    ready => 2,
    _ => 3,
  };
}

class FoodOrderItem {
  const FoodOrderItem({
    required this.menuItemId,
    required this.name,
    required this.quantity,
    required this.unitPriceMinorLkr,
  });

  factory FoodOrderItem.fromJson(Map<String, dynamic> j) => FoodOrderItem(
    menuItemId: j['menuItemId'] as String,
    name: j['name'] as String,
    quantity: j['quantity'] as int,
    unitPriceMinorLkr: j['unitPriceMinorLkr'] as int,
  );

  final String menuItemId, name;
  final int quantity, unitPriceMinorLkr;
}

class FoodPickupTime {
  const FoodPickupTime({this.scheduledArrival, this.estimatedArrival, this.etaEvidenceAt});

  factory FoodPickupTime.fromJson(Map<String, dynamic> j) => FoodPickupTime(
    scheduledArrival: _instant(j['scheduledArrival']),
    estimatedArrival: _instant(j['estimatedArrival']),
    etaEvidenceAt: _instant(j['etaEvidenceAt']),
  );

  final DateTime? scheduledArrival, estimatedArrival, etaEvidenceAt;
}

/// A passenger's own order as `OrderDto` reports it. `pickupCode` is only ever present here, in the
/// authenticated own-order view, and is never written to local storage.
class FoodOrder {
  const FoodOrder({
    required this.id,
    required this.status,
    required this.statusSequence,
    required this.collectionPointId,
    required this.journeyId,
    required this.pickupStopId,
    required this.createdAt,
    required this.updatedAt,
    this.pickupCode,
    this.items,
    this.totalMinorLkr,
    this.pickupTime,
    this.reasonCode,
  });

  factory FoodOrder.fromJson(Map<String, dynamic> j) => FoodOrder(
    id: j['id'] as String,
    status: j['status'] as String,
    statusSequence: j['statusSequence'] as int,
    collectionPointId: j['collectionPointId'] as String,
    journeyId: j['journeyId'] as String,
    pickupStopId: j['pickupStopId'] as String,
    pickupCode: j['pickupCode'] as String?,
    items: (j['items'] as List?)?.map((i) => FoodOrderItem.fromJson(i as Map<String, dynamic>)).toList(),
    totalMinorLkr: j['totalMinorLkr'] as int?,
    pickupTime: j['pickupTime'] == null ? null : FoodPickupTime.fromJson(j['pickupTime'] as Map<String, dynamic>),
    reasonCode: j['reasonCode'] as String?,
    createdAt: DateTime.parse(j['createdAt'] as String),
    updatedAt: DateTime.parse(j['updatedAt'] as String),
  );

  final String id, status, collectionPointId, journeyId, pickupStopId;
  final int statusSequence;
  final String? pickupCode, reasonCode;
  final List<FoodOrderItem>? items;
  final int? totalMinorLkr;
  final FoodPickupTime? pickupTime;
  final DateTime createdAt, updatedAt;

  bool get isTerminal => OrderStatus.isTerminal(status);
}

class DraftItem {
  const DraftItem(this.menuItemId, this.quantity);
  final String menuItemId;
  final int quantity;
}

/// The immutable body of a submission: only what the passenger chose. The ETA, cutoff and account
/// are never part of it; SmartRail derives them. Items are sorted so the same basket always
/// produces the same bytes under one request key.
class CanonicalOrder {
  CanonicalOrder({
    required this.collectionPointId,
    required this.journeyId,
    required this.pickupStopId,
    required List<DraftItem> items,
  }) : items = List.unmodifiable([...items]..sort((a, b) => a.menuItemId.compareTo(b.menuItemId)));

  factory CanonicalOrder.fromJson(Map<String, dynamic> j) => CanonicalOrder(
    collectionPointId: j['collectionPointId'] as String,
    journeyId: j['journeyId'] as String,
    pickupStopId: j['pickupStopId'] as String,
    items: [
      for (final i in j['items'] as List) DraftItem((i as Map)['menuItemId'] as String, i['quantity'] as int),
    ],
  );

  final String collectionPointId, journeyId, pickupStopId;
  final List<DraftItem> items;

  Map<String, dynamic> toJson() => {
    'collectionPointId': collectionPointId,
    'journeyId': journeyId,
    'pickupStopId': pickupStopId,
    'items': [
      for (final i in items) {'menuItemId': i.menuItemId, 'quantity': i.quantity},
    ],
  };

  /// The POST body: the canonical order plus the client request key. Nothing else.
  Map<String, dynamic> toRequestJson(String clientRequestKey) => {
    'clientRequestKey': clientRequestKey,
    ...toJson(),
  };
}

class DisplayLine {
  const DisplayLine({required this.name, required this.quantity, required this.unitPriceMinorLkr});

  factory DisplayLine.fromJson(Map<String, dynamic> j) => DisplayLine(
    name: j['name'] as String,
    quantity: j['quantity'] as int,
    unitPriceMinorLkr: j['unitPriceMinorLkr'] as int,
  );

  final String name;
  final int quantity, unitPriceMinorLkr;

  Map<String, dynamic> toJson() => {'name': name, 'quantity': quantity, 'unitPriceMinorLkr': unitPriceMinorLkr};
}

/// What to show while the provider's answer is not known yet (a PLACING order carries no items).
class OrderDisplay {
  const OrderDisplay({
    required this.pointName,
    required this.trainNumber,
    required this.serviceDate,
    required this.lines,
  });

  factory OrderDisplay.fromJson(Map<String, dynamic> j) => OrderDisplay(
    pointName: j['pointName'] as String,
    trainNumber: j['trainNumber'] as String,
    serviceDate: j['serviceDate'] as String,
    lines: [for (final l in j['lines'] as List) DisplayLine.fromJson(l as Map<String, dynamic>)],
  );

  final String pointName, trainNumber, serviceDate;
  final List<DisplayLine> lines;

  int get totalMinorLkr => lines.fold(0, (sum, l) => sum + l.quantity * l.unitPriceMinorLkr);

  Map<String, dynamic> toJson() => {
    'pointName': pointName,
    'trainNumber': trainNumber,
    'serviceDate': serviceDate,
    'lines': [for (final l in lines) l.toJson()],
  };
}

/// A basket frozen for submission, not yet given a key.
class OrderDraft {
  const OrderDraft({required this.order, required this.display});
  final CanonicalOrder order;
  final OrderDisplay display;
}

/// The one unresolved submission of a signed-in account: its request key and frozen payload,
/// saved before the first send so a lost response, repeated taps or a restart all reuse the same
/// key. It never holds a pickup code. It lives until the outcome is conclusive.
class PendingFoodRequest {
  const PendingFoodRequest({
    required this.clientRequestKey,
    required this.order,
    required this.display,
    this.orderId,
    this.lostAttempts = 0,
  });

  factory PendingFoodRequest.fromJson(Map<String, dynamic> j) => PendingFoodRequest(
    clientRequestKey: j['clientRequestKey'] as String,
    order: CanonicalOrder.fromJson(j['order'] as Map<String, dynamic>),
    display: OrderDisplay.fromJson(j['display'] as Map<String, dynamic>),
    orderId: j['orderId'] as String?,
    lostAttempts: j['lostAttempts'] as int? ?? 0,
  );

  factory PendingFoodRequest.decode(String raw) => PendingFoodRequest.fromJson(jsonDecode(raw) as Map<String, dynamic>);

  final String clientRequestKey;
  final CanonicalOrder order;
  final OrderDisplay display;

  /// The stable order link, once a 202 or a later read told us its ID.
  final String? orderId;

  /// Attempts whose outcome was never learned. Above zero, an authentication failure on a retry
  /// says nothing about whether an earlier attempt was processed, so the request is kept.
  final int lostAttempts;

  PendingFoodRequest copyWith({String? orderId, int? lostAttempts}) => PendingFoodRequest(
    clientRequestKey: clientRequestKey,
    order: order,
    display: display,
    orderId: orderId ?? this.orderId,
    lostAttempts: lostAttempts ?? this.lostAttempts,
  );

  Map<String, dynamic> toJson() => {
    'clientRequestKey': clientRequestKey,
    'order': order.toJson(),
    'display': display.toJson(),
    'orderId': orderId,
    'lostAttempts': lostAttempts,
  };

  String encode() => jsonEncode(toJson());
}

DateTime? _instant(Object? value) => value == null ? null : DateTime.parse(value as String);
