# v0.5.0 startup hotfix

- Fixes the WPF startup exception caused by a TwoWay `ProgressBar.Value` binding targeting the read-only `CateringInventoryRowViewModel.Progress` property.
- Adds CI/release validation requiring display-only `ProgressBar` bindings to declare `Mode=OneWay`.
- Keeps the public release at v0.5.0 by rebuilding and replacing the existing installer and portable ZIP in place.
