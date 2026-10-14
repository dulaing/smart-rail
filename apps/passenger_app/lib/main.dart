import 'package:flutter/material.dart';

import 'auth/account_api.dart';
import 'auth/auth_service.dart';

void main() {
  const config = AuthConfig();
  runApp(
    MainApp(
      auth: KeycloakAuthService(config),
      accounts: HttpAccountApi(config.apiBaseUrl),
    ),
  );
}

class MainApp extends StatelessWidget {
  const MainApp({super.key, required this.auth, required this.accounts});

  final AuthService auth;
  final AccountApi accounts;

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: 'SmartRail',
      home: HomePage(auth: auth, accounts: accounts),
    );
  }
}

/// A passenger starts as a guest. Signing in is optional, and cancelling or signing out
/// returns here, so public information never depends on an account.
class HomePage extends StatefulWidget {
  const HomePage({super.key, required this.auth, required this.accounts});

  final AuthService auth;
  final AccountApi accounts;

  @override
  State<HomePage> createState() => _HomePageState();
}

class _HomePageState extends State<HomePage> {
  AccountResult? _result;
  String? _notice;
  bool _busy = false;

  @override
  void initState() {
    super.initState();
    _restore();
  }

  Future<void> _restore() async {
    if (await widget.auth.restore()) {
      await _loadAccount();
    }
  }

  Future<void> _loadAccount() async {
    final token = await widget.auth.accessToken();
    final result = await widget.accounts.fetchMe(token);
    if (mounted) setState(() => _result = result);
  }

  Future<void> _signIn() async {
    setState(() {
      _busy = true;
      _notice = null;
    });
    try {
      await widget.auth.signIn();
      await _loadAccount();
    } catch (_) {
      // Cancelled or failed: stay in guest mode.
      if (mounted) setState(() => _notice = 'Sign-in was cancelled or failed.');
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _signOut() async {
    setState(() => _busy = true);
    try {
      await widget.auth.signOut();
    } catch (_) {
      // Local tokens are already cleared; the browser step may have been cancelled.
    }
    if (mounted) {
      setState(() {
        _result = null;
        _busy = false;
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final result = _result;
    return Scaffold(
      appBar: AppBar(title: const Text('SmartRail')),
      body: Center(
        child: Padding(
          padding: const EdgeInsets.all(24),
          child: switch (result) {
            AccountFound(:final account) => _signedIn(account),
            AccountMissing() => _message(
              'You are signed in, but there is no SmartRail account for you.',
            ),
            AccountFailed(:final message) => _message(message),
            null => _guest(),
          },
        ),
      ),
    );
  }

  Widget _guest() => Column(
    mainAxisSize: MainAxisSize.min,
    children: [
      const Text('Guest', style: TextStyle(fontSize: 22)),
      const SizedBox(height: 8),
      const Text('Timetables and train times are available without an account.'),
      if (_notice != null) ...[
        const SizedBox(height: 8),
        Text(_notice!, key: const Key('notice')),
      ],
      const SizedBox(height: 16),
      FilledButton(
        onPressed: _busy ? null : _signIn,
        child: const Text('Sign in'),
      ),
    ],
  );

  Widget _signedIn(Account account) => Column(
    mainAxisSize: MainAxisSize.min,
    children: [
      Text(account.displayName, style: const TextStyle(fontSize: 22)),
      const SizedBox(height: 8),
      Text(account.roles.join(', ')),
      const SizedBox(height: 16),
      OutlinedButton(
        onPressed: _busy ? null : _signOut,
        child: const Text('Sign out'),
      ),
    ],
  );

  Widget _message(String text) => Column(
    mainAxisSize: MainAxisSize.min,
    children: [
      Text(text, textAlign: TextAlign.center),
      const SizedBox(height: 16),
      OutlinedButton(
        onPressed: _busy ? null : _signOut,
        child: const Text('Back to guest'),
      ),
    ],
  );
}
