# HisabKitab invoice/UI update

Implemented in the supplied source project:

- Party photo upload during party creation; photo shown in party detail.
- Business/company logo upload during company setup and logo replacement from company list.
- Shared Billing/Shipping address modal with bounded height, internal scrolling and animated open/close.
- New invoice address entry is persisted as a PartyAddress for the selected party.
- Invoice template modal is bounded, scrollable and animated.
- Six invoice layouts are seeded per company/document type:
  - Classic Professional
  - Purple Modern
  - Teal Professional (product-image capable)
  - Curved Teal (product-image capable)
  - Curved Navy
  - Classic Blue Panel
- Attached template images are bundled as preview assets.
- Native Android A4 PDF renderer rebuilt around the selected template, with wrapped product names, adaptive table columns, Indian currency formatting, amount in words, company logo and optional product images.
- New bills store the selected TemplateId and snapshot ProductImagePath.
- Older bill detail views can fall back to the current product-master image when no image snapshot exists.
- Discount supports Percent and Amount entry modes.
- Indian comma/rupee formatting is centralized in InvoiceFormatService.
- Sales/Purchase list cards show outstanding balance.
- Party list and party detail show calculated outstanding balance.
- Invoice detail shows balance and amount in words.
- Party mobile/email are clickable from invoice detail.
- Invoice detail opens from the right and closes back to the list with a right-side slide animation.
- Bill search uses the existing floating result-box pattern.
- Invoice count badge/list count layout remains in a fixed right column to avoid overlap.

Build note: the supplied environment does not contain the .NET SDK/MSBuild, so this package was statically checked (XAML XML parsing, handler-name checks and source brace checks) but not compiled on-device in this environment.
