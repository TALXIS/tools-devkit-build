# Versioning

```
<MAJOR>.<MINOR>.(<branch>)<YY><MM>.<number-of-commits>
```

* `MAJOR`, `MINOR` - Inferred from `Version` provided in the `*proj` file or `Directory.Build.props`
* `branch` - Optional number to identify a major branch, the higher, the more production - this is used to "protect" production from accidental manual deploys, since in deploying a solution with lower version will fail by default. Maximum value is `5` due to [build number limitation](https://learn.microsoft.com/en-us/archive/blogs/msbuild/why-are-build-numbers-limited-to-65535) in Windows.
* `YY`, `MM` - Parts of last commit's to the project date, eg. `2509`
* `number-of-commits` - Total number of commits since the first day of the month the last commit was made (includes commits from referenced projects). There is a limit of `65,535` commits per month (again due to size), *this will be addressed in future*.

## Solutions

## Plugins

## PCFs

Since PCFs [use semantic versioning](https://learn.microsoft.com/en-us/power-apps/developer/component-framework/manifest-schema-reference/control), and there are [some nuances](https://dianabirkelbach.wordpress.com/2020/12/23/all-about-pcf-versioning/) with changing the major and minor numbers, we opted to flatten the version into the `PATCH` part of the version, thus resulting version of the control in the manifest will be `0.0.X`. This may be changed in future, so please reach out of you need to work with the version number in any way.

## Edge cases

There may obviously be some edge cases. If you find any, please [report them](https://github.com/TALXIS/tools-devkit-build/issues) or submit a PR to fix it!

### Removing a project reference results in a lower version number on the same day
If you make a change to a solution with a referenced project on the same day, and then remove a project reference on the same day, the second build version is going to be lower and the solution will fail to import. This is most likely to happen in non-production branches and the known workaround is to make a commit and rebuild the affected solution the next day. This is to change in future.