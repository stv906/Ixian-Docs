# Ixian Documentation Pages

**Ixian-Docs** is the official documentation website for the **Ixian Network**. It is built using **[Next.js](https://nextjs.org/)** and the open-source documentation starter kit **[rubix-documents](https://github.com/rubixvi/rubix-documents)**.

---

## Getting Started

To start development:

1. Clone the repository:
   ```sh
   git clone git@github.com:ixian-platform/Ixian-Docs.git
   ```
2. Navigate into the project directory:
   ```sh
   cd Ixian-Docs
   ```
3. Install dependencies:
   ```sh
   npm install
   ```
4. Start the development server:
   ```sh
   npm run dev
   ```

## Building the Project

To build the project for production, run:
1. Build the app:

    ```bash
     npm run build
     ```

2. Start the production server:

    ```bash
    npm run start
    ```

---

## Configuration

### Website Settings
Website settings can be modified in:
```
/settings/settings.ts
```

### Navigation
The navbar navigation can be customized by editing:
```
/settings/navigation.tsx
```

## Managing Documentation

### Adding or Editing Documentation
1. Add or edit corresponding `.mdx` files in:
   ```
   /contents/docs/
   ```
2. To regenerate index after modifying the documentation, run:
   ```bash
   npm run gen:docs
   ```

This will update the documents data in:
```
/settings/documents.ts
```

### Updating Search Data
To regenerate search data after modifying the documentation, run:
```bash
npm run generate-content-json
```

This will update the search data in:
```
/public/search-data/documents.json
```

---

## Contributing

We welcome contributions from developers, integrators, and builders.

1. Fork this repository
2. Create a feature branch ('feature/my-change')
3. Commit with clear, descriptive messages
4. Open a Pull Request for review

---

## License
This project is open-source and follows the **[rubix-documents](https://github.com/rubixvi/rubix-documents)** license. See the full license in:
```
/LICENSE
```

---

## Documentation Reference
For further details, refer to the official **Next.js** documentation v15+ using App Router: [Next.js Documentation](https://nextjs.org/docs).
