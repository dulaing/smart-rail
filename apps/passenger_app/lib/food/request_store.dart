import 'package:flutter_secure_storage/flutter_secure_storage.dart';

import 'order_models.dart';

/// Where an account's one unresolved food submission is kept. It is private to that account: one
/// entry per account ID, so another account never reads it, and it holds no pickup code.
abstract interface class FoodRequestStore {
  Future<PendingFoodRequest?> read(String accountId);
  Future<void> write(String accountId, PendingFoodRequest request);
  Future<void> clear(String accountId);
}

/// Secure storage rather than the public preferences used for timetable data, so the request is
/// never mixed into the public cache.
class SecureFoodRequestStore implements FoodRequestStore {
  SecureFoodRequestStore({FlutterSecureStorage? storage}) : _storage = storage ?? const FlutterSecureStorage();

  final FlutterSecureStorage _storage;

  static String _key(String accountId) => 'food.request.$accountId';

  @override
  Future<PendingFoodRequest?> read(String accountId) async {
    final raw = await _storage.read(key: _key(accountId));
    if (raw == null) return null;
    try {
      return PendingFoodRequest.decode(raw);
    } catch (_) {
      // An unreadable record cannot be replayed. It is not deleted here, so nothing is lost
      // silently, but the app can only treat the account as having no replayable request.
      return null;
    }
  }

  @override
  Future<void> write(String accountId, PendingFoodRequest request) =>
      _storage.write(key: _key(accountId), value: request.encode());

  @override
  Future<void> clear(String accountId) => _storage.delete(key: _key(accountId));
}

/// Holds requests in memory. Tests keep the store and rebuild the controller to model a restart.
class MemoryFoodRequestStore implements FoodRequestStore {
  final Map<String, String> entries = {};
  bool failWrites = false;

  @override
  Future<PendingFoodRequest?> read(String accountId) async {
    final raw = entries[accountId];
    return raw == null ? null : PendingFoodRequest.decode(raw);
  }

  @override
  Future<void> write(String accountId, PendingFoodRequest request) async {
    if (failWrites) throw StateError('storage unavailable');
    entries[accountId] = request.encode();
  }

  @override
  Future<void> clear(String accountId) async => entries.remove(accountId);
}
