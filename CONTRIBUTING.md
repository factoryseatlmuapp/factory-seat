# Contributing

Thanks for wanting to help! Here's where everything goes.

## Ideas and questions

- **Got an idea?** Post it in [Discussions › Ideas](https://github.com/factoryseatlmuapp/factory-seat/discussions/categories/ideas). 👍 the ideas you'd like to see; that's how they get picked for a release.
- **Not sure how something works?** Ask in [Discussions › Q&A](https://github.com/factoryseatlmuapp/factory-seat/discussions/categories/q-a).
- **Want to show off your career?** There's [Show and tell](https://github.com/factoryseatlmuapp/factory-seat/discussions/categories/show-and-tell).

## Bugs

[Open an issue](https://github.com/factoryseatlmuapp/factory-seat/issues/new/choose) with the bug report form. The [README](README.md#reporting-a-bug) lists what helps most, like your version and the race's results file.

## Translations

Translations are the easiest way to help, and they're very welcome. Korean, German, French, Spanish, and Brazilian Portuguese ship with the app so far.

The how-to is in [src/LmuCareer.App/locales/README.md](src/LmuCareer.App/locales/README.md): copy `_template.json`, translate, and try it in the app. Then post it in [Discussions › Ideas](https://github.com/factoryseatlmuapp/factory-seat/discussions/categories/ideas) or open a pull request. Corrections to an existing language are welcome too.

## Code

Please **open an issue or an Idea before writing code.** Releases follow a plan, so a change that doesn't fit the current one may have to wait, and it's better to find that out before you spend an evening on it. Once it's agreed:

- You need the .NET 10 SDK on Windows. `dotnet test tests/LmuCareer.Core.Tests` should pass.
- Keep a pull request to one change.
- Any new text the player sees needs to be in `_template.json` (`python tools/i18n.py template`).

By contributing, you agree your work is released under the project's [MIT License](LICENSE).

Everyone here follows the [Code of conduct](CODE_OF_CONDUCT.md).
