import 'dart:async';
import 'dart:math';

import 'package:flutter/foundation.dart';

import '../account/account_controller.dart';
import '../auth/account_api.dart';
import 'order_api.dart';
import 'order_models.dart';
import 'request_store.dart';

/// Where the one unresolved submission stands.
enum OrderPhase {
  /// Nothing in flight or unresolved.
  none,

  /// The first send is under way.
  submitting,

  /// The outcome is not known: no answer arrived, or the order link is still PLACING. The request
  /// is kept and recoverable with the same key.
  uncertain,

  /// The server said this key belongs to a different order. Kept for the person to read; never
  /// worked around with a fresh key.
  conflict,

  /// Conclusively refused; [FoodOrdersController.refusalCode] says why. Nothing was placed.
  refused,

  /// The sign-in was rejected. Private content is hidden until the person signs in again.
  sessionEnded,

  /// Signed in, but this account may not order.
  forbidden,

  /// The request could not be saved, so it was not sent at all.
  storageFailed,
}

/// A signed-in passenger's own food orders: submits one durable request, recovers it through lost
/// responses and restarts, and holds the order the server reports.
///
/// Everything here belongs to one account at a time. When the account changes (sign-out, a
/// different account, a rejected session) the in-memory state is dropped at once and any answer
/// still in flight is ignored, so one account's order, pickup code or pending payload can never
/// show up under, or be submitted as, another.
class FoodOrdersController extends ChangeNotifier {
  FoodOrdersController({
    required this.account,
    required this.api,
    required this.store,
    String Function()? newKey,
    this.pollEvery = const Duration(seconds: 30),
  }) : _newKey = newKey ?? _randomUuid {
    account.addListener(_onAccountChanged);
    _onAccountChanged();
  }

  final AccountController account;
  final FoodOrderSource api;
  final FoodRequestStore store;
  final String Function() _newKey;

  /// How often an unresolved request is checked while the Food tab is visible.
  final Duration pollEvery;
  Timer? _timer;
  bool _visible = false;

  String? _accountId;
  int _epoch = 0;
  bool _busy = false;
  bool _listing = false;
  bool _disposed = false;
  VoidCallback? _onCommitted;

  OrderPhase phase = OrderPhase.none;

  /// The unresolved submission, if any. At most one exists per account.
  PendingFoodRequest? request;

  /// The stable `code` of a conclusive refusal, when [phase] is [OrderPhase.refused].
  String? refusalCode;

  final Map<String, FoodOrder> _orders = {};
  String? _selectedId;

  /// The order being shown, once the provider confirmed it. Shown only in this authenticated view.
  FoodOrder? get order => _selectedId == null ? null : _orders[_selectedId];

  /// The account's own orders as the server last reported them, newest first (a bounded list, not a
  /// full history).
  List<FoodOrder> get recentOrders =>
      (_orders.values.toList()..sort((a, b) => b.createdAt.compareTo(a.createdAt)));

  /// A cancellation is under way for the shown order.
  bool cancelling = false;

  /// Why the last cancellation did not happen: a stable code, `PROVIDER_UNAVAILABLE`, or
  /// `UNCONFIRMED` when the reply was lost and a reread did not settle it.
  String? cancelProblem;

  /// True while a recovery read or replay is running (the button spins; the key never changes).
  bool checking = false;

  /// True while the order screen should be showing rather than browse or review.
  bool viewing = false;

  String? get accountId => _accountId;
  bool get signedIn => _accountId != null;

  /// Whether this account is allowed to place orders. Unknown permissions do not block: the
  /// server decides and answers 403 if not.
  bool get mayOrder {
    final result = account.result;
    if (result is! AccountFound) return false;
    return result.account.permissions.isEmpty || result.account.permissions.contains('orders.manage');
  }

  void _onAccountChanged() {
    final id = account.accountId;
    if (id != _accountId) {
      unawaited(_attach(id));
    } else if (id != null && (phase == OrderPhase.sessionEnded || phase == OrderPhase.forbidden) && !account.busy) {
      // The same account signed in again after its session was rejected.
      if (request != null) {
        unawaited(recover());
      } else {
        phase = OrderPhase.none;
        _notify();
        unawaited(refreshOrders());
      }
    }
  }

  /// Drops everything held for the previous account and loads the new one's saved request.
  Future<void> _attach(String? id) async {
    _epoch++;
    _accountId = id;
    _busy = false;
    checking = false;
    request = null;
    _orders.clear();
    _selectedId = null;
    cancelling = false;
    cancelProblem = null;
    _listing = false;
    refusalCode = null;
    phase = OrderPhase.none;
    viewing = false;
    _onCommitted = null;
    _notify();
    if (id == null) return;

    final epoch = _epoch;
    PendingFoodRequest? stored;
    try {
      stored = await store.read(id);
    } catch (_) {
      stored = null;
    }
    if (epoch != _epoch || _disposed) return;
    if (stored != null) {
      // Whether the earlier attempt reached the server before the app stopped is unknowable.
      request = stored.copyWith(lostAttempts: stored.lostAttempts < 1 ? 1 : stored.lostAttempts);
      phase = OrderPhase.uncertain;
      viewing = true;
      _notify();
      await recover();
    }
    await refreshOrders();
  }

  /// Freezes the basket under a fresh request key, saves it, then sends it. [onCommitted] runs once
  /// the request is durable and may have reached the server, so the caller can clear its basket.
  ///
  /// It does nothing while another submission is unresolved: repeated taps, and any edit made while
  /// waiting, can never mint a second key.
  Future<void> submit(OrderDraft draft, {VoidCallback? onCommitted}) async {
    final id = _accountId;
    if (id == null || _busy || request != null) return;
    _busy = true;
    final epoch = _epoch;
    phase = OrderPhase.submitting;
    refusalCode = null;
    viewing = true;
    _onCommitted = onCommitted;
    _notify();

    final record = PendingFoodRequest(clientRequestKey: _newKey(), order: draft.order, display: draft.display);
    try {
      // Saved before the first send: a crash or a lost reply must find this same key again.
      await store.write(id, record);
    } catch (_) {
      if (epoch == _epoch) {
        _busy = false;
        phase = OrderPhase.storageFailed;
        _notify();
      }
      return;
    }
    if (epoch != _epoch) {
      _busy = false;
      return;
    }
    request = record;
    await _attempt(record, epoch);
  }

  /// Asks again about the unresolved request with the same key and payload: replays it when no link
  /// ID is known yet, otherwise reads the link. Safe to repeat; it never creates a second order.
  Future<void> recover() async {
    final current = request;
    if (current == null || _busy || _accountId == null) return;
    _busy = true;
    checking = true;
    _notify();
    await _attempt(current, _epoch);
  }

  Future<void> _attempt(PendingFoodRequest r, int epoch) async {
    OrderReply reply;
    try {
      final token = await account.auth.accessToken();
      reply = r.orderId == null
          ? await api.place(token, r.order, r.clientRequestKey)
          : await api.read(token, r.orderId!);
    } on StateError {
      // No usable sign-in token: nothing was sent.
      reply = const OrderReply(status: 401);
    } catch (_) {
      reply = const OrderReply.lost();
    }
    if (epoch != _epoch || _disposed) return;
    try {
      await _interpret(r, reply);
    } finally {
      if (epoch == _epoch) {
        _busy = false;
        checking = false;
        _notify();
      }
    }
  }

  Future<void> _interpret(PendingFoodRequest r, OrderReply reply) async {
    final status = reply.status;
    if (status == null) return _lost(r);
    if (status == 401 || status == 403) return _authFailed(r, status);

    if (r.orderId == null) {
      // A placement (or a replay of one whose link ID we never learned).
      switch (status) {
        case 200 || 201 || 202:
          final placed = reply.order;
          if (placed == null) return _lost(r);
          if (status == 202 || placed.status == OrderStatus.placing) return _stillPlacing(r, placed);
          if (placed.status == OrderStatus.rejected) return _refused(r, placed.reasonCode);
          return _confirmed(placed);
        case 409:
          return _conflict();
        case 400 || 404 || 422:
          return _refused(r, reply.code);
      }
      return _lost(r);
    }

    // The link ID is known: read it.
    switch (status) {
      case 200:
        final read = reply.order;
        if (read == null) return _lost(r);
        if (read.status == OrderStatus.placing) return _stillPlacing(r, read);
        if (read.status == OrderStatus.rejected) return _refused(r, read.reasonCode);
        return _confirmed(read);
      case 404:
        return _refused(r, reply.code ?? 'ORDER_NOT_FOUND');
    }
    return _lost(r);
  }

  /// No answer, or an answer that settles nothing: keep the request and say so.
  Future<void> _lost(PendingFoodRequest r) async {
    final next = r.copyWith(lostAttempts: r.lostAttempts + 1);
    await _persist(next);
    request = next;
    phase = OrderPhase.uncertain;
    _committed();
  }

  /// 202 or a PLACING read: the provider has not answered. Remember the stable link.
  Future<void> _stillPlacing(PendingFoodRequest r, FoodOrder placing) async {
    final next = r.orderId == placing.id ? r : r.copyWith(orderId: placing.id);
    if (!identical(next, r)) await _persist(next);
    request = next;
    phase = OrderPhase.uncertain;
    _committed();
  }

  Future<void> _confirmed(FoodOrder confirmed) async {
    await _clearRequest();
    _selectedId = confirmed.id;
    _merge(confirmed);
    phase = OrderPhase.none;
    viewing = true;
    _committed();
  }

  Future<void> _refused(PendingFoodRequest r, String? code) async {
    await _clearRequest();
    refusalCode = code ?? 'UNKNOWN';
    phase = OrderPhase.refused;
    _onCommitted = null;
  }

  Future<void> _conflict() async {
    phase = OrderPhase.conflict;
    _committed();
  }

  Future<void> _authFailed(PendingFoodRequest r, int status) async {
    final neverReachedServer = r.lostAttempts == 0 && r.orderId == null;
    if (neverReachedServer) {
      // Turned away at the door on the very first attempt: nothing was ever created.
      await _clearRequest();
      _onCommitted = null;
    }
    _sessionLost(status);
  }

  /// A 401 or 403: private content (orders, pickup codes) is dropped from memory at once. A kept
  /// request stays saved for the same account to recover after signing in again.
  void _sessionLost(int status) {
    _orders.clear();
    _selectedId = null;
    cancelling = false;
    phase = status == 401 ? OrderPhase.sessionEnded : OrderPhase.forbidden;
  }

  void _committed() {
    final callback = _onCommitted;
    _onCommitted = null;
    callback?.call();
  }

  Future<void> _persist(PendingFoodRequest next) async {
    final id = _accountId;
    if (id == null) return;
    try {
      await store.write(id, next);
    } catch (_) {
      // The in-memory copy still holds the key; a replay finds the same link either way.
    }
  }

  Future<void> _clearRequest() async {
    final id = _accountId;
    request = null;
    if (id == null) return;
    try {
      await store.clear(id);
    } catch (_) {
      // A stale record only ever replays to the same already-final answer.
    }
  }

  /// Accepts a server answer about an order only if it moves that order forward. A late response
  /// can never undo Ready, Collected, No-show or Cancelled, and a terminal order is never revived.
  /// Returns the order now held.
  FoodOrder _merge(FoodOrder incoming) {
    final held = _orders[incoming.id];
    if (held != null && !_isNewer(incoming, held)) return held;
    _orders[incoming.id] = incoming;
    return incoming;
  }

  static bool _isNewer(FoodOrder incoming, FoodOrder held) {
    if (held.isTerminal && incoming.status != held.status) return false;
    if (OrderStatus.rank(incoming.status) < OrderStatus.rank(held.status)) return false;
    if (incoming.statusSequence != held.statusSequence) return incoming.statusSequence > held.statusSequence;
    // Same sequence: only fresher details (for example a new pickup estimate) of the same state.
    return incoming.status == held.status && incoming.updatedAt.isAfter(held.updatedAt);
  }

  /// Reads the account's own orders and merges what is newer. Orders still PLACING are the
  /// unresolved request's business ([recover]), not shown as confirmed here.
  Future<void> refreshOrders() async {
    final id = _accountId;
    if (id == null || _listing) return;
    _listing = true;
    final epoch = _epoch;
    try {
      OrderReply reply;
      try {
        reply = await api.list(await account.auth.accessToken());
      } on StateError {
        reply = const OrderReply(status: 401);
      } catch (_) {
        reply = const OrderReply.lost();
      }
      if (epoch != _epoch || _disposed) return;
      if (reply.status == 401 || reply.status == 403) {
        _sessionLost(reply.status!);
      } else if (reply.status == 200) {
        for (final listed in reply.orders ?? const <FoodOrder>[]) {
          if (listed.status == OrderStatus.placing || listed.status == OrderStatus.rejected) continue;
          _merge(listed);
        }
      }
    } finally {
      if (epoch == _epoch) {
        _listing = false;
        _notify();
      }
    }
  }

  /// Shows an order from the list. Its details were already read from the server.
  void select(String orderId) {
    if (!_orders.containsKey(orderId)) return;
    _selectedId = orderId;
    cancelProblem = null;
    viewing = true;
    _notify();
  }

  /// Asks the server to cancel the shown order, once. The app never decides eligibility or the
  /// provider cutoff: it rereads the order, sends one cancellation and reports what the server says.
  /// After a lost reply it rereads before offering another try.
  Future<void> cancel() async {
    final id = _accountId, shown = order;
    if (id == null || shown == null || cancelling || shown.isTerminal) return;
    cancelling = true;
    cancelProblem = null;
    final epoch = _epoch;
    _notify();
    try {
      final token = await account.auth.accessToken();
      // Freshest state first: it may already be cancelled, collected or past cancellation.
      final fresh = await api.read(token, shown.id);
      if (epoch != _epoch || _disposed) return;
      if (fresh.status == 200 && fresh.order != null) {
        final now = _merge(fresh.order!);
        if (now.isTerminal) return;
      }
      final reply = await api.cancel(token, shown.id);
      if (epoch != _epoch || _disposed) return;
      switch (reply.status) {
        case 200:
          final cancelled = reply.order;
          if (cancelled != null) {
            _merge(cancelled);
          } else {
            cancelProblem = 'UNCONFIRMED';
          }
        case 409:
          cancelProblem = reply.code ?? 'INVALID_STATE';
        case 503:
          cancelProblem = 'PROVIDER_UNAVAILABLE';
        case 404:
          cancelProblem = 'ORDER_NOT_FOUND';
        case 401 || 403:
          _sessionLost(reply.status!);
        case null:
          await _reconcileCancellation(token, shown.id, epoch);
        default:
          cancelProblem = 'UNCONFIRMED';
      }
    } on StateError {
      if (epoch == _epoch) _sessionLost(401);
    } catch (_) {
      if (epoch == _epoch) cancelProblem = 'UNCONFIRMED';
    } finally {
      if (epoch == _epoch) {
        cancelling = false;
        _notify();
      }
    }
  }

  /// The cancellation reply was lost: read the order to learn whether it took effect.
  Future<void> _reconcileCancellation(String token, String orderId, int epoch) async {
    final reread = await api.read(token, orderId);
    if (epoch != _epoch || _disposed) return;
    final read = reread.status == 200 ? reread.order : null;
    if (read != null) {
      final now = _merge(read);
      if (now.status == OrderStatus.cancelled) return;
    }
    cancelProblem = 'UNCONFIRMED';
  }

  /// Food is on screen and the app is in the foreground. Becoming visible (a tab switch or a resume)
  /// checks at once; while visible an unresolved request is checked every [pollEvery]. Nothing runs
  /// in the background.
  void setVisible(bool visible) {
    if (visible == _visible) return;
    _visible = visible;
    _timer?.cancel();
    _timer = null;
    if (!visible) return;
    _timer = Timer.periodic(pollEvery, (_) => unawaited(tick()));
    unawaited(tick());
  }

  /// One refresh round: re-ask about an unresolved request. A rejected session or a key conflict is
  /// not retried on a timer.
  Future<void> tick() async {
    if (request != null && phase == OrderPhase.uncertain) await recover();
    if (_accountId != null && (_orders.isEmpty || _orders.values.any((o) => !o.isTerminal))) {
      await refreshOrders();
    }
  }

  /// The person explicitly gives up a request the server said belongs to a different order. This is
  /// the only way past a key conflict, and it is never automatic.
  Future<void> discardConflict() async {
    if (phase != OrderPhase.conflict) return;
    await _clearRequest();
    phase = OrderPhase.none;
    viewing = false;
    _notify();
  }

  /// Leaves a refusal or storage failure and returns to the basket, which was kept.
  void dismissProblem() {
    if (phase == OrderPhase.refused || phase == OrderPhase.storageFailed || phase == OrderPhase.sessionEnded || phase == OrderPhase.forbidden) {
      if (request == null) {
        phase = OrderPhase.none;
        refusalCode = null;
        viewing = false;
        _notify();
      }
    }
  }

  void dismissCancelProblem() {
    cancelProblem = null;
    _notify();
  }

  /// Leaves the order screen. An unresolved request keeps being checked and stays reachable from Food.
  void closeOrder() {
    viewing = false;
    _notify();
  }

  void openOrder() {
    viewing = true;
    _notify();
  }

  void _notify() {
    if (!_disposed) notifyListeners();
  }

  @override
  void dispose() {
    _disposed = true;
    _timer?.cancel();
    account.removeListener(_onAccountChanged);
    super.dispose();
  }
}

/// A random (version 4) UUID for a client request key. The server only needs it unique per account.
String _randomUuid() {
  final random = Random.secure();
  final bytes = List<int>.generate(16, (_) => random.nextInt(256));
  bytes[6] = (bytes[6] & 0x0f) | 0x40;
  bytes[8] = (bytes[8] & 0x3f) | 0x80;
  String hex(int from, int to) => bytes.sublist(from, to).map((b) => b.toRadixString(16).padLeft(2, '0')).join();
  return '${hex(0, 4)}-${hex(4, 6)}-${hex(6, 8)}-${hex(8, 10)}-${hex(10, 16)}';
}
