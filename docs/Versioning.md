# Versioning

## Solutions

## Plugins

## PCFs

Since PCFs [use semantic versioning](https://learn.microsoft.com/en-us/power-apps/developer/component-framework/manifest-schema-reference/control), and there are [some nuances](https://dianabirkelbach.wordpress.com/2020/12/23/all-about-pcf-versioning/) with changing the major and minor numbers, we opted to flatten the version into the `PATCH` part of the version, thus resulting version of the control in the manifest will be `0.0.X`. This may be changed in future, so please reach out of you need to work with the version number in any way.

## Edge cases