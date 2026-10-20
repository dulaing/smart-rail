/// One place on a dated train where food can be collected, as `GET /api/journeys/{id}/collection-options`
/// reports it. `orderable` and `reasonCode` come from the server: the app never decides eligibility.
class CollectionOption {
  const CollectionOption({
    required this.stopId,
    required this.stationId,
    required this.pointId,
    required this.pointName,
    required this.cutoffMinutes,
    required this.orderable,
    this.scheduledArrival,
    this.estimatedArrival,
    this.orderBy,
    this.reasonCode,
  });

  factory CollectionOption.fromJson(Map<String, dynamic> j) {
    final point = j['collectionPoint'] as Map<String, dynamic>;
    return CollectionOption(
      stopId: j['stopId'] as String,
      stationId: j['stationId'] as String,
      pointId: point['id'] as String,
      pointName: point['name'] as String,
      cutoffMinutes: point['cutoffMinutes'] as int,
      scheduledArrival: _instant(j['scheduledArrival']),
      estimatedArrival: _instant(j['estimatedArrival']),
      orderBy: _instant(j['orderBy']),
      orderable: j['orderable'] as bool,
      reasonCode: j['reasonCode'] as String?,
    );
  }

  final String stopId, stationId, pointId, pointName;
  final int cutoffMinutes;
  final DateTime? scheduledArrival, estimatedArrival, orderBy;
  final bool orderable;
  final String? reasonCode;
}

class CollectionOptions {
  const CollectionOptions({
    required this.journeyId,
    required this.trainNumber,
    required this.serviceDate,
    required this.options,
  });

  factory CollectionOptions.fromJson(Map<String, dynamic> j) =>
      CollectionOptions(
        journeyId: j['journeyId'] as String,
        trainNumber: j['trainNumber'] as String,
        serviceDate: j['serviceDate'] as String,
        options: [
          for (final o in j['options'] as List)
            CollectionOption.fromJson(o as Map<String, dynamic>),
        ],
      );

  final String journeyId, trainNumber, serviceDate;
  final List<CollectionOption> options;
}

class FoodMenuItem {
  const FoodMenuItem({
    required this.id,
    required this.name,
    required this.priceMinorLkr,
    required this.stockQuantity,
  });

  factory FoodMenuItem.fromJson(Map<String, dynamic> j) => FoodMenuItem(
    id: j['id'] as String,
    name: j['name'] as String,
    priceMinorLkr: j['priceMinorLkr'] as int,
    stockQuantity: j['stockQuantity'] as int,
  );

  final String id, name;
  final int priceMinorLkr, stockQuantity;
}

class FoodMenu {
  const FoodMenu({
    required this.collectionPointId,
    required this.currency,
    required this.items,
  });

  factory FoodMenu.fromJson(Map<String, dynamic> j) => FoodMenu(
    collectionPointId: j['collectionPointId'] as String,
    currency: j['currency'] as String,
    items: [
      for (final i in j['items'] as List)
        FoodMenuItem.fromJson(i as Map<String, dynamic>),
    ],
  );

  final String collectionPointId, currency;
  final List<FoodMenuItem> items;
}

/// Prices travel as integer minor LKR (65000 = Rs 650), so they are formatted from integers and
/// never pass through a floating-point rupee value.
String formatLkr(int minor) {
  final negative = minor < 0;
  final abs = minor.abs();
  final rupees = (abs ~/ 100).toString();
  final grouped = StringBuffer();
  for (var i = 0; i < rupees.length; i++) {
    if (i > 0 && (rupees.length - i) % 3 == 0) grouped.write(',');
    grouped.write(rupees[i]);
  }
  final cents = (abs % 100).toString().padLeft(2, '0');
  final amount = abs % 100 == 0 ? grouped.toString() : '$grouped.$cents';
  return '${negative ? '-' : ''}LKR $amount';
}

DateTime? _instant(Object? value) =>
    value == null ? null : DateTime.parse(value as String);
