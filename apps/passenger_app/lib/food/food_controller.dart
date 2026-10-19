import 'package:flutter/foundation.dart';

import '../trains/train_controller.dart';
import 'food_api.dart';
import 'food_models.dart';

enum FoodLoad { idle, loading, ready, providerUnavailable, notFound, failed }

/// One dated train the passenger can order on. The date matters: SIM-3001 today and tomorrow are
/// different journeys with different IDs, and only today's can take an order.
class FoodJourneyChoice {
  const FoodJourneyChoice({
    required this.id,
    required this.serviceDate,
    required this.trainNumber,
    required this.originCode,
    required this.finalCode,
    required this.onRoute,
  });

  final String id, trainNumber, originCode, finalCode;
  final DateTime serviceDate;

  /// True when the Trains tab is currently showing this journey for the chosen route.
  final bool onRoute;
}

/// Public Food browsing: pick a dated journey, see the collection options the server offers on it,
/// pick one and build a cart from its menu. Nothing here needs an account, and everything here is
/// replaced by newer server answers; eligibility and prices are never decided in the app.
///
/// Stale answers are dropped: changing the journey or the pickup while a read is in flight makes
/// the earlier response irrelevant.
class FoodController extends ChangeNotifier {
  FoodController({required this.source, required this.trains});

  /// The most of one item a single order may carry; the server enforces the same limit.
  static const maxQuantity = 20;
  static const maxItems = 20;

  final FoodSource source;
  final TrainController trains;

  FoodJourneyChoice? journey;
  CollectionOptions? options;
  FoodLoad optionsLoad = FoodLoad.idle;
  String? _stopId;

  FoodMenu? menu;
  FoodLoad menuLoad = FoodLoad.idle;

  /// Quantity per menu item ID. Only items with a quantity above zero are present.
  final Map<String, int> _cart = {};

  /// True once a menu refresh removed or reduced something already in the cart.
  bool cartAdjusted = false;

  int _journeyVersion = 0;
  int _menuVersion = 0;
  bool _disposed = false;

  /// The collection option chosen on the current journey, or null.
  CollectionOption? get selectedOption {
    final stopId = _stopId;
    if (stopId == null) return null;
    return options?.options.where((o) => o.stopId == stopId).firstOrNull;
  }

  Map<String, int> get cart => Map.unmodifiable(_cart);

  int get cartCount => _cart.values.fold(0, (sum, q) => sum + q);

  int quantityOf(String itemId) => _cart[itemId] ?? 0;

  /// The total in minor LKR from the current menu prices, or null while there is no menu.
  int? get totalMinorLkr {
    final current = menu;
    if (current == null) return null;
    var total = 0;
    for (final item in current.items) {
      total += item.priceMinorLkr * (_cart[item.id] ?? 0);
    }
    return total;
  }

  /// Items in the cart with their current menu entry, in menu order.
  List<(FoodMenuItem, int)> get cartLines {
    final current = menu;
    if (current == null) return const [];
    return [
      for (final item in current.items)
        if ((_cart[item.id] ?? 0) > 0) (item, _cart[item.id]!),
    ];
  }

  /// Upcoming journeys the passenger can choose from: the saved timetable plus whatever the Trains
  /// tab is showing now, from today (Colombo) onwards.
  List<FoodJourneyChoice> choices() {
    final today = _todayKey;
    final live = {for (final t in trains.liveTrains) t.journeyId};
    final byId = <String, FoodJourneyChoice>{};
    for (final j in trains.saved.journeys) {
      if (_dayKey(j.serviceDate) < today || j.stops.isEmpty) continue;
      byId[j.id] = FoodJourneyChoice(
        id: j.id,
        serviceDate: j.serviceDate,
        trainNumber: j.trainNumber,
        originCode: j.stops.first.stationCode,
        finalCode: j.stops.last.stationCode,
        onRoute: live.contains(j.id),
      );
    }
    for (final t in trains.liveTrains) {
      byId.putIfAbsent(
        t.journeyId,
        () => FoodJourneyChoice(
          id: t.journeyId,
          serviceDate: t.serviceDate,
          trainNumber: t.trainNumber,
          originCode: trains.station(trains.originId)?.code ?? '',
          finalCode: trains.station(trains.destinationId)?.code ?? '',
          onRoute: true,
        ),
      );
    }
    final list = byId.values.toList()
      ..sort((a, b) {
        final byDate = _dayKey(a.serviceDate).compareTo(_dayKey(b.serviceDate));
        if (byDate != 0) return byDate;
        if (a.onRoute != b.onRoute) return a.onRoute ? -1 : 1;
        return a.trainNumber.compareTo(b.trainNumber);
      });
    return list;
  }

  /// Colombo's calendar date now as yyyymmdd.
  int get _todayKey {
    final local = trains.now.add(const Duration(hours: 5, minutes: 30));
    return _dayKey(local);
  }

  /// A service date is a plain calendar date; compare its fields, not its time zone.
  static int _dayKey(DateTime d) => d.year * 10000 + d.month * 100 + d.day;

  bool isToday(DateTime serviceDate) => _dayKey(serviceDate) == _todayKey;

  /// Chooses a dated journey and reads its collection options. Everything chosen for the previous
  /// journey (pickup, menu, cart) is dropped, and a slow answer for it is ignored.
  Future<void> selectJourney(FoodJourneyChoice choice) async {
    if (journey?.id == choice.id) return;
    journey = choice;
    _journeyVersion++;
    _menuVersion++;
    options = null;
    _stopId = null;
    menu = null;
    menuLoad = FoodLoad.idle;
    _cart.clear();
    cartAdjusted = false;
    optionsLoad = FoodLoad.loading;
    _notify();
    await _readOptions(choice.id, _journeyVersion);
  }

  /// Reads the collection options again for the current journey, keeping the pickup and cart.
  Future<void> refreshOptions() async {
    final current = journey;
    if (current == null) return;
    final version = ++_journeyVersion;
    if (options == null) {
      optionsLoad = FoodLoad.loading;
      _notify();
    }
    await _readOptions(current.id, version);
  }

  Future<void> _readOptions(String journeyId, int version) async {
    try {
      final read = await source.collectionOptions(journeyId);
      if (_disposed || version != _journeyVersion) return;
      options = read;
      optionsLoad = FoodLoad.ready;
      // A pickup that is no longer offered at all (for example the stop left the list) is dropped.
      if (_stopId != null && selectedOption == null) _clearPickup();
    } on FoodReadException catch (e) {
      if (_disposed || version != _journeyVersion) return;
      optionsLoad = _loadFor(e.failure);
      // Keep older options on screen when only a refresh failed; they are marked stale by the page.
      if (optionsLoad == FoodLoad.notFound) options = null;
    }
    _notify();
  }

  /// Opens the menu for an option. Unavailable options cannot be chosen; the reason stays visible.
  Future<void> selectOption(CollectionOption option) async {
    if (!option.orderable) return;
    if (_stopId == option.stopId) return;
    _stopId = option.stopId;
    _menuVersion++;
    menu = null;
    _cart.clear();
    cartAdjusted = false;
    menuLoad = FoodLoad.loading;
    _notify();
    await _readMenu(option.pointId, _menuVersion);
  }

  /// Leaves the menu and goes back to the pickup list; the cart is kept for the same pickup.
  void deselectOption() {
    _clearPickup();
    _notify();
  }

  void _clearPickup() {
    _stopId = null;
    _menuVersion++;
    menu = null;
    menuLoad = FoodLoad.idle;
    _cart.clear();
    cartAdjusted = false;
  }

  /// Reads the menu again for the chosen pickup and reconciles the cart with it.
  Future<void> refreshMenu() async {
    final option = selectedOption;
    if (option == null) return;
    final version = ++_menuVersion;
    if (menu == null) {
      menuLoad = FoodLoad.loading;
      _notify();
    }
    await _readMenu(option.pointId, version);
  }

  Future<void> _readMenu(String pointId, int version) async {
    try {
      final read = await source.menu(pointId);
      if (_disposed || version != _menuVersion) return;
      menu = read;
      menuLoad = FoodLoad.ready;
      _reconcileCart(read);
    } on FoodReadException catch (e) {
      if (_disposed || version != _menuVersion) return;
      menuLoad = _loadFor(e.failure);
      if (menuLoad == FoodLoad.notFound) menu = null;
    }
    _notify();
  }

  /// Stock and menu contents can change between reads: drop what is gone and cap the rest.
  void _reconcileCart(FoodMenu fresh) {
    final byId = {for (final i in fresh.items) i.id: i};
    for (final id in _cart.keys.toList()) {
      final item = byId[id];
      final limit = item == null ? 0 : _limitFor(item);
      if (limit == 0) {
        _cart.remove(id);
        cartAdjusted = true;
      } else if (_cart[id]! > limit) {
        _cart[id] = limit;
        cartAdjusted = true;
      }
    }
  }

  static int _limitFor(FoodMenuItem item) => item.stockQuantity < maxQuantity ? item.stockQuantity : maxQuantity;

  /// The most of this item that can be added now.
  int limitFor(FoodMenuItem item) => _limitFor(item);

  void setQuantity(FoodMenuItem item, int quantity) {
    final capped = quantity.clamp(0, _limitFor(item));
    if (capped == 0) {
      _cart.remove(item.id);
    } else if (_cart.containsKey(item.id) || _cart.length < maxItems) {
      _cart[item.id] = capped;
    }
    cartAdjusted = false;
    _notify();
  }

  void clearCart() {
    _cart.clear();
    cartAdjusted = false;
    _notify();
  }

  FoodLoad _loadFor(FoodReadFailure failure) => switch (failure) {
    FoodReadFailure.providerUnavailable => FoodLoad.providerUnavailable,
    FoodReadFailure.notFound => FoodLoad.notFound,
    _ => FoodLoad.failed,
  };

  /// Whether the cart can go to review: a pickup the server still calls orderable and something in it.
  bool get canReview => (selectedOption?.orderable ?? false) && _cart.isNotEmpty && menu != null;

  void _notify() {
    if (!_disposed) notifyListeners();
  }

  @override
  void dispose() {
    _disposed = true;
    super.dispose();
  }
}
