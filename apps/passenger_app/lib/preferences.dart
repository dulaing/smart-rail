import 'package:flutter/material.dart';
import 'package:shared_preferences/shared_preferences.dart';

/// The languages the passenger app is written in. Sinhala and Tamil wording is unreviewed.
const supportedLanguageCodes = ['en', 'si', 'ta'];

/// Where device preferences are kept. They belong to this phone, not to an account, so signing
/// in, out or switching accounts never changes them.
abstract class PreferencesStore {
  Future<ThemeMode> loadThemeMode();
  Future<String?> loadLanguageCode();
  Future<void> saveThemeMode(ThemeMode mode);
  Future<void> saveLanguageCode(String? code);
}

class SharedPreferencesStore implements PreferencesStore {
  static const _themeKey = 'device.themeMode';
  static const _languageKey = 'device.languageCode';

  @override
  Future<ThemeMode> loadThemeMode() async {
    final prefs = await SharedPreferences.getInstance();
    final name = prefs.getString(_themeKey);
    return ThemeMode.values.firstWhere((m) => m.name == name, orElse: () => ThemeMode.system);
  }

  @override
  Future<String?> loadLanguageCode() async {
    final code = (await SharedPreferences.getInstance()).getString(_languageKey);
    return supportedLanguageCodes.contains(code) ? code : null;
  }

  @override
  Future<void> saveThemeMode(ThemeMode mode) async =>
      (await SharedPreferences.getInstance()).setString(_themeKey, mode.name);

  @override
  Future<void> saveLanguageCode(String? code) async {
    final prefs = await SharedPreferences.getInstance();
    if (code == null) {
      await prefs.remove(_languageKey);
    } else {
      await prefs.setString(_languageKey, code);
    }
  }
}

/// Holds preferences in memory. Used by tests to model a restart: keep the store, rebuild the app.
class MemoryPreferencesStore implements PreferencesStore {
  ThemeMode themeMode = ThemeMode.system;
  String? languageCode;

  @override
  Future<ThemeMode> loadThemeMode() async => themeMode;

  @override
  Future<String?> loadLanguageCode() async => languageCode;

  @override
  Future<void> saveThemeMode(ThemeMode mode) async => themeMode = mode;

  @override
  Future<void> saveLanguageCode(String? code) async => languageCode = code;
}

/// The current theme and language choice. Follows the device until the person picks otherwise.
class PreferencesController extends ChangeNotifier {
  PreferencesController(this._store);

  final PreferencesStore _store;
  ThemeMode _themeMode = ThemeMode.system;
  String? _languageCode;
  bool _disposed = false;

  ThemeMode get themeMode => _themeMode;

  /// Null means "follow the device language".
  Locale? get locale => _languageCode == null ? null : Locale(_languageCode!);

  Future<void> load() async {
    final mode = await _store.loadThemeMode();
    final code = await _store.loadLanguageCode();
    if (_disposed) return;
    _themeMode = mode;
    _languageCode = code;
    notifyListeners();
  }

  Future<void> setThemeMode(ThemeMode mode) async {
    _themeMode = mode;
    notifyListeners();
    await _store.saveThemeMode(mode);
  }

  Future<void> setLanguageCode(String code) async {
    _languageCode = code;
    notifyListeners();
    await _store.saveLanguageCode(code);
  }

  @override
  void dispose() {
    _disposed = true;
    super.dispose();
  }
}
