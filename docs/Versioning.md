# Versioning

```
<MAJOR>.<MINOR>.(<branch>)<YY><MM>.<DD><number-of-commits>
```

* `MAJOR`, `MINOR` - Inferred from `Version` provided in the `*proj` file or `Directory.Build.props`
* `branch` - Optional number to identify a major branch, the higher, the more production - this is used to "protect" production from accidental manual deploys, since in deploying a solution with lower version will fail by default. Maximum value is `5` due to [build number limitation](https://learn.microsoft.com/en-us/archive/blogs/msbuild/why-are-build-numbers-limited-to-65535) in Windows.
* `YY`, `MM`, `DD` - Parts of last commit's to the project date, eg. `2509`
* `number-of-commits` - Total number of commits (formated as `000`) in the day the last commit was made (includes commits from referenced projects). There is a limit of `999` commits per day (again due to size).

## Solutions

## Plugins

## PCFs

Since PCFs [use semantic versioning](https://learn.microsoft.com/en-us/power-apps/developer/component-framework/manifest-schema-reference/control), and there are [some nuances](https://dianabirkelbach.wordpress.com/2020/12/23/all-about-pcf-versioning/) with changing the major and minor numbers. The maximum value for each part is *2,147,483,647* (32-bit integer). With PCFs it is impossible to push a lower version of PCF (even with `ForceUpdate=TRUE`). We currently assemble the PCF version  as following from the outputs generated above (this applies also when not using the generate version):

```
0.0.<SECONDS_FROM_2020-01-01_TILL_LAST_COMMIT_OR_NOW>
```

## Edge cases

There may obviously be some edge cases. If you find any, please [report them](https://github.com/TALXIS/tools-devkit-build/issues) or submit a PR to fix it!

### Removing a project reference results in a lower version number on the same day
If you make a change to a solution with a referenced project on the same day, and then remove a project reference on the same day, the second build version is going to be lower and the solution will fail to import. This is most likely to happen in non-production branches and the known workaround is to make a commit and rebuild the affected solution the next day. This is to change in future.

### Over 999 commits per day
Each number of version is limited by [`ushort`](https://learn.microsoft.com/en-us/dotnet/api/system.uint16?view=net-9.0)'s maximum size. If you do more than 999 commits per day across all referenced projects, you will end up with an error. A workaround is to bump projects with too many commits in that day to a single commit the next day. Alternatively, you can consider using [squashing commits](https://docs.github.com/en/pull-requests/collaborating-with-pull-requests/incorporating-changes-from-a-pull-request/about-pull-request-merges#squash-and-merge-your-commits). If you hit this, please reach out to us.