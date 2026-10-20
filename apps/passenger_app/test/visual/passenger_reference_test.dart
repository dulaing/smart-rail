import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:passenger_app/account/account_controller.dart';
import 'package:passenger_app/auth/account_api.dart';
import 'package:passenger_app/auth/auth_service.dart';
import 'package:passenger_app/food/food_api.dart';
import 'package:passenger_app/food/food_controller.dart';
import 'package:passenger_app/food/food_models.dart';
import 'package:passenger_app/food/order_api.dart';
import 'package:passenger_app/food/order_models.dart';
import 'package:passenger_app/food/orders_controller.dart';
import 'package:passenger_app/food/request_store.dart';
import 'package:passenger_app/l10n/app_localizations.dart';
import 'package:passenger_app/preferences.dart';
import 'package:passenger_app/shell.dart';
import 'package:passenger_app/trains/train_api.dart';
import 'package:passenger_app/trains/train_cache.dart';
import 'package:passenger_app/trains/train_controller.dart';
import 'package:passenger_app/trains/train_models.dart';
import 'package:smartrail_core/smartrail_core.dart';

final _now = DateTime.utc(2025, 10, 13, 1, 36);
final _date = DateTime(2025, 10, 13);

const _stations = [
  PassengerStation(
    id: 'kandy',
    code: 'Kandy',
    nameEn: 'Kandy',
    nameSi: 'මහනුවර',
    nameTa: 'கண்டி',
  ),
  PassengerStation(
    id: 'pjn',
    code: 'Peradeniya Jn',
    nameEn: 'Peradeniya Jn',
    nameSi: 'පේරාදෙණිය හන්දිය',
    nameTa: 'பேராதனை சந்திப்பு',
  ),
  PassengerStation(
    id: 'vey',
    code: 'Veyangoda',
    nameEn: 'Veyangoda',
    nameSi: 'වේයන්ගොඩ',
    nameTa: 'வேயங்கொட',
  ),
  PassengerStation(
    id: 'gmp',
    code: 'Gampaha',
    nameEn: 'Gampaha',
    nameSi: 'ගම්පහ',
    nameTa: 'கம்பஹா',
  ),
  PassengerStation(
    id: 'rgm',
    code: 'Ragama',
    nameEn: 'Ragama',
    nameSi: 'රාගම',
    nameTa: 'ராகம',
  ),
  PassengerStation(
    id: 'fot',
    code: 'FOT',
    nameEn: 'Colombo Fort',
    nameSi: 'කොළඹ කොටුව',
    nameTa: 'கொழும்பு கோட்டை',
  ),
];

final _journey = PassengerJourney(
  id: 'journey-1',
  serviceDate: _date,
  trainNumber: 'DS-B1',
  sourceLabel: 'Public railway timetable',
  stops: [
    PassengerStop(
      id: 'vey-stop',
      stationId: 'vey',
      stationCode: 'Veyangoda',
      sequence: 1,
      arrival: DateTime.utc(2025, 10, 13, 1, 55),
      departure: DateTime.utc(2025, 10, 13, 1, 56),
    ),
    PassengerStop(
      id: 'gmp-stop',
      stationId: 'gmp',
      stationCode: 'Gampaha',
      sequence: 2,
      arrival: DateTime.utc(2025, 10, 13, 2, 10),
      departure: DateTime.utc(2025, 10, 13, 2, 11),
    ),
    PassengerStop(
      id: 'rgm-stop',
      stationId: 'rgm',
      stationCode: 'Ragama',
      sequence: 3,
      arrival: DateTime.utc(2025, 10, 13, 2, 30),
      departure: DateTime.utc(2025, 10, 13, 2, 31),
    ),
    PassengerStop(
      id: 'fot-stop',
      stationId: 'fot',
      stationCode: 'FOT',
      sequence: 4,
      arrival: DateTime.utc(2025, 10, 13, 2, 48),
    ),
  ],
);

final _foodJourney = PassengerJourney(
  id: 'food-journey',
  serviceDate: _date,
  trainNumber: 'DS-E1',
  sourceLabel: 'Public railway timetable',
  stops: [
    PassengerStop(
      id: 'kandy-stop',
      stationId: 'kandy',
      stationCode: 'Kandy',
      sequence: 1,
      departure: DateTime.utc(2025, 10, 13, 4, 40),
    ),
    PassengerStop(
      id: 'pjn-stop',
      stationId: 'pjn',
      stationCode: 'Peradeniya Jn',
      sequence: 2,
      arrival: DateTime.utc(2025, 10, 13, 5, 12),
      departure: DateTime.utc(2025, 10, 13, 5, 13),
    ),
    PassengerStop(
      id: 'food-fot-stop',
      stationId: 'fot',
      stationCode: 'FOT',
      sequence: 3,
      arrival: DateTime.utc(2025, 10, 13, 8, 30),
    ),
  ],
);

final _train = PassengerLiveTrain(
  journeyId: _journey.id,
  serviceDate: _date,
  trainNumber: _journey.trainNumber,
  originStopId: 'gmp-stop',
  destinationStopId: 'fot-stop',
  scheduledDeparture: DateTime.utc(2025, 10, 13, 1, 40),
  scheduledArrival: DateTime.utc(2025, 10, 13, 2, 18),
  estimatedDeparture: DateTime.utc(2025, 10, 13, 1, 57),
  estimatedArrival: DateTime.utc(2025, 10, 13, 2, 48),
  lastObservedAt: _now.add(const Duration(minutes: 1)),
  freshness: 'CURRENT',
  overdue: false,
  availability: 'RUNNING',
  plannedPlatform: '2',
  confirmedPlatform: '3',
  confirmedPlatformAt: _now.subtract(const Duration(minutes: 2)),
  finalStopId: 'fot-stop',
);

final _firstTrain = PassengerLiveTrain(
  journeyId: 'journey-a1',
  serviceDate: _date,
  trainNumber: 'DS-A1',
  originStopId: 'gmp-stop',
  destinationStopId: 'fot-stop',
  scheduledDeparture: DateTime.utc(2025, 10, 13, 1, 54),
  scheduledArrival: DateTime.utc(2025, 10, 13, 2, 35),
  estimatedDeparture: DateTime.utc(2025, 10, 13, 1, 54),
  estimatedArrival: DateTime.utc(2025, 10, 13, 2, 35),
  lastObservedAt: _now,
  freshness: 'CURRENT',
  overdue: false,
  availability: 'RUNNING',
  plannedPlatform: '2',
);

class _Auth implements AuthService {
  @override
  Future<String> accessToken() async => 'fixture-token';
  @override
  Future<bool> restore() async => false;
  @override
  Future<void> signIn() async {}
  @override
  Future<void> signOut() async {}
}

class _Accounts implements AccountApi {
  @override
  Future<AccountResult> fetchMe(String accessToken) async =>
      const AccountMissing();
}

class _TrainSource implements PassengerTrainSource {
  @override
  Future<List<PassengerStation>> stations() async => _stations;
  @override
  Future<List<PassengerJourney>> timetable(DateTime serviceDate) async => [
    _journey,
  ];
  @override
  Future<String?> lastKnownStation(String journeyId) async => 'vey';
  @override
  Future<ApiRead<RouteSnapshot>> live(
    String origin,
    String destination,
    String? etag,
  ) async => ApiRead(
    RouteSnapshot(
      originId: origin,
      destinationId: destination,
      trains: [_train],
      notices: const [],
      observedAt: _now,
      downloadedAt: _now,
    ),
    'fixture-etag',
    _now,
  );
}

class _FoodSource implements FoodSource {
  @override
  Future<CollectionOptions> collectionOptions(String journeyId) async =>
      CollectionOptions(
        journeyId: journeyId,
        trainNumber: _foodJourney.trainNumber,
        serviceDate: '2025-10-13',
        options: [
          CollectionOption(
            stopId: 'pjn-stop',
            stationId: 'pjn',
            pointId: 'railbites-pjn',
            pointName: 'Peradeniya Jn',
            cutoffMinutes: 30,
            scheduledArrival: _foodJourney.stops[1].arrival,
            estimatedArrival: _foodJourney.stops[1].arrival,
            orderBy: DateTime.utc(2025, 10, 13, 4, 42),
            orderable: true,
          ),
        ],
      );
  @override
  Future<FoodMenu> menu(String collectionPointId) async => const FoodMenu(
    collectionPointId: 'railbites-pjn',
    currency: 'LKR',
    items: [
      FoodMenuItem(
        id: 'rice',
        name: 'Vegetable rice & curry',
        priceMinorLkr: 65000,
        stockQuantity: 30,
      ),
      FoodMenuItem(
        id: 'tea',
        name: 'Plain tea',
        priceMinorLkr: 12000,
        stockQuantity: 30,
      ),
      FoodMenuItem(
        id: 'bun',
        name: 'Fish bun',
        priceMinorLkr: 18000,
        stockQuantity: 30,
      ),
      FoodMenuItem(
        id: 'roti',
        name: 'Egg roti',
        priceMinorLkr: 22000,
        stockQuantity: 0,
      ),
    ],
  );
}

class _Orders implements FoodOrderSource {
  @override
  Future<OrderReply> cancel(String token, String orderId) async =>
      const OrderReply.lost();
  @override
  Future<OrderReply> list(String token) async => const OrderReply.lost();
  @override
  Future<OrderReply> place(
    String token,
    CanonicalOrder order,
    String clientRequestKey,
  ) async => const OrderReply.lost();
  @override
  Future<OrderReply> read(String token, String orderId) async =>
      const OrderReply.lost();
}

class _RequestStore implements FoodRequestStore {
  @override
  Future<void> clear(String accountId) async {}
  @override
  Future<PendingFoodRequest?> read(String accountId) async => null;
  @override
  Future<void> write(String accountId, PendingFoodRequest request) async {}
}

Future<
  ({
    Widget app,
    FoodController food,
    AccountController account,
    PreferencesController preferences,
    TrainController trains,
    FoodOrdersController orders,
  })
>
_fixture(ThemeMode themeMode) async {
  final auth = _Auth();
  final account = AccountController(auth: auth, accounts: _Accounts());
  final preferences = PreferencesController(MemoryPreferencesStore())
    ..setThemeMode(themeMode);
  final trains =
      TrainController(
          api: _TrainSource(),
          cache: _MemoryTrainCache(),
          clock: () => _now,
        )
        ..stations = _stations
        ..originId = 'gmp'
        ..destinationId = 'fot'
        ..liveTrains = [_firstTrain, _train]
        ..observedAt = _now
        ..saved = PublicTrainCache(journeys: [_journey, _foodJourney]);
  final food = FoodController(source: _FoodSource(), trains: trains);
  final choice = food.choices().firstWhere(
    (choice) => choice.id == _foodJourney.id,
  );
  await food.selectJourney(choice);
  await food.selectOption(food.options!.options.single);
  food.setQuantity(food.menu!.items[0], 1);
  food.setQuantity(food.menu!.items[1], 1);
  final orders = FoodOrdersController(
    account: account,
    api: _Orders(),
    store: _RequestStore(),
    newKey: () => 'reference-request',
  );
  return (
    app: MaterialApp(
      theme: srTheme(
        SrProfile.passenger,
        Brightness.light,
        locale: const Locale('en'),
      ),
      darkTheme: srTheme(
        SrProfile.passenger,
        Brightness.dark,
        locale: const Locale('en'),
      ),
      themeMode: themeMode,
      debugShowCheckedModeBanner: false,
      locale: const Locale('en'),
      supportedLocales: AppLocalizations.supportedLocales,
      localizationsDelegates: AppLocalizations.localizationsDelegates,
      home: PassengerShell(
        account: account,
        preferences: preferences,
        trains: trains,
        food: food,
        orders: orders,
      ),
    ),
    food: food,
    account: account,
    preferences: preferences,
    trains: trains,
    orders: orders,
  );
}

void _referenceViewport(WidgetTester tester) {
  tester.view.physicalSize = const Size(1080, 2250);
  tester.view.devicePixelRatio = 2.5;
  addTearDown(tester.view.reset);
}

class _MemoryTrainCache implements PassengerTrainCache {
  @override
  Future<PublicTrainCache> read() async =>
      PublicTrainCache(journeys: [_journey, _foodJourney]);
  @override
  Future<void> saveStations(
    List<PassengerStation> stations,
    DateTime at,
  ) async {}
  @override
  Future<void> saveTimetable(
    List<PassengerJourney> journeys,
    DateTime at,
  ) async {}
  @override
  Future<void> saveNotices(
    List<PassengerNotice> notices,
    DateTime at,
    String originId,
    String destinationId,
  ) async {}
}

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  setUpAll(() async {
    for (final (family, file) in [
      ('NotoSans', 'NotoSans-Variable.ttf'),
      ('AbhayaLibre', 'AbhayaLibre-Regular.ttf'),
      ('NotoSansTamil', 'NotoSansTamil-Variable.ttf'),
    ]) {
      await (FontLoader('packages/smartrail_core/$family')
            ..addFont(rootBundle.load('packages/smartrail_core/fonts/$file')))
          .load();
    }
    await (FontLoader(
      'MaterialIcons',
    )..addFont(rootBundle.load('fonts/MaterialIcons-Regular.otf'))).load();
  });
  testWidgets('home light reference viewport', (tester) async {
    _referenceViewport(tester);
    final f = await _fixture(ThemeMode.light);
    await tester.pumpWidget(f.app);
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('tab-home')));
    await tester.pumpAndSettle();
    await expectLater(
      find.byType(MaterialApp),
      matchesGoldenFile('goldens/home-light.png'),
    );
    f.account.dispose();
    f.preferences.dispose();
    f.trains.dispose();
    f.food.dispose();
    f.orders.dispose();
  });

  testWidgets('home dark reference viewport', (tester) async {
    _referenceViewport(tester);
    final f = await _fixture(ThemeMode.dark);
    await tester.pumpWidget(f.app);
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('tab-home')));
    await tester.pumpAndSettle();
    await expectLater(
      find.byType(MaterialApp),
      matchesGoldenFile('goldens/home-dark.png'),
    );
    f.account.dispose();
    f.preferences.dispose();
    f.trains.dispose();
    f.food.dispose();
    f.orders.dispose();
  });

  testWidgets('train detail reference viewport', (tester) async {
    _referenceViewport(tester);
    final f = await _fixture(ThemeMode.light);
    await tester.pumpWidget(f.app);
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('tab-trains')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(Key('train-${_journey.id}')));
    await tester.pumpAndSettle();
    await expectLater(
      find.byType(MaterialApp),
      matchesGoldenFile('goldens/detail.png'),
    );
    f.account.dispose();
    f.preferences.dispose();
    f.trains.dispose();
    f.food.dispose();
    f.orders.dispose();
  });

  testWidgets('guest account reference viewport', (tester) async {
    _referenceViewport(tester);
    final f = await _fixture(ThemeMode.light);
    await tester.pumpWidget(f.app);
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('tab-account')));
    await tester.pumpAndSettle();
    await expectLater(
      find.byType(MaterialApp),
      matchesGoldenFile('goldens/account-guest.png'),
    );
    f.account.dispose();
    f.preferences.dispose();
    f.trains.dispose();
    f.food.dispose();
    f.orders.dispose();
  });

  testWidgets('guest account dark reference viewport', (tester) async {
    _referenceViewport(tester);
    final f = await _fixture(ThemeMode.dark);
    await tester.pumpWidget(f.app);
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('tab-account')));
    await tester.pumpAndSettle();
    await expectLater(
      find.byType(MaterialApp),
      matchesGoldenFile('goldens/account-guest-dark.png'),
    );
    f.account.dispose();
    f.preferences.dispose();
    f.trains.dispose();
    f.food.dispose();
    f.orders.dispose();
  });

  testWidgets('food menu reference viewport', (tester) async {
    _referenceViewport(tester);
    final f = await _fixture(ThemeMode.light);
    await tester.pumpWidget(f.app);
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('tab-food')));
    await tester.pumpAndSettle();
    await expectLater(
      find.byType(MaterialApp),
      matchesGoldenFile('goldens/food-menu.png'),
    );
    f.account.dispose();
    f.preferences.dispose();
    f.trains.dispose();
    f.food.dispose();
    f.orders.dispose();
  });

  testWidgets('food menu dark reference viewport', (tester) async {
    _referenceViewport(tester);
    final f = await _fixture(ThemeMode.dark);
    await tester.pumpWidget(f.app);
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('tab-food')));
    await tester.pumpAndSettle();
    await expectLater(
      find.byType(MaterialApp),
      matchesGoldenFile('goldens/food-menu-dark.png'),
    );
    f.account.dispose();
    f.preferences.dispose();
    f.trains.dispose();
    f.food.dispose();
    f.orders.dispose();
  });

  testWidgets('food review reference viewport', (tester) async {
    _referenceViewport(tester);
    final f = await _fixture(ThemeMode.light);
    await tester.pumpWidget(f.app);
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('tab-food')));
    await tester.pumpAndSettle();
    f.food.reviewing = true;
    f.food.notifyListeners();
    await tester.pumpAndSettle();
    await expectLater(
      find.byType(MaterialApp),
      matchesGoldenFile('goldens/food-review.png'),
    );
    f.account.dispose();
    f.preferences.dispose();
    f.trains.dispose();
    f.food.dispose();
    f.orders.dispose();
  });

  testWidgets(
    'appearance choices remain reachable below the Account reference',
    (tester) async {
      _referenceViewport(tester);
      final f = await _fixture(ThemeMode.light);
      await tester.pumpWidget(f.app);
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('tab-account')));
      await tester.pumpAndSettle();

      await tester.dragUntilVisible(
        find.byKey(const Key('appearance-title')),
        find.byKey(const Key('account-scroll')),
        const Offset(0, -400),
      );
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('theme-system')), findsOneWidget);
      expect(find.byKey(const Key('theme-light')), findsOneWidget);
      expect(find.byKey(const Key('theme-dark')), findsOneWidget);

      await tester.tap(find.byKey(const Key('theme-dark')));
      await tester.pumpAndSettle();
      expect(f.preferences.themeMode, ThemeMode.dark);
      await tester.tap(find.byKey(const Key('theme-system')));
      await tester.pumpAndSettle();
      expect(f.preferences.themeMode, ThemeMode.system);

      f.account.dispose();
      f.preferences.dispose();
      f.trains.dispose();
      f.food.dispose();
      f.orders.dispose();
    },
  );
}
