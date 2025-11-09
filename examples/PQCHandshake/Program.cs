using IXICore;
using IXICore.Meta;
using System;
using System.Linq;
using System.Text;

class PostQuantumHandshakeDemo
{
    static readonly byte[] IXI_AES_KEY_INFO = Encoding.UTF8.GetBytes("IXI-AES-KEY");
    static readonly byte[] IXI_CHACHA_KEY_INFO = Encoding.UTF8.GetBytes("IXI-CHACHA-KEY");
    static byte[] Concat(params byte[][] arrays)
    {
        int total = arrays.Sum(a => a.Length);
        var result = new byte[total];
        int offset = 0;
        foreach (var a in arrays)
        {
            Buffer.BlockCopy(a, 0, result, offset, a.Length);
            offset += a.Length;
        }
        return result;
    }

    static void Main(string[] args)
    {
        Console.WriteLine("=== Ixian Post-Quantum Handshake Demo ===\n");

        Logging.consoleOutput = false;

        // Initialize crypto
        CryptoManager.initLib();

        // Simulate a simplistic contact handshake
        DemoContactHandshake();

        // Show encryption with derived keys
        DemoEncryption();

        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
    }

    static void DemoContactHandshake()
    {
        Console.WriteLine("## Contact Handshake (with ML-KEM)\n");

        // === STEP 1: Alice and Bob generate long-term RSA keys ===        
        Console.WriteLine("STEP 1: Generate wallets and addresses");
        var aliceRsaKeys = CryptoManager.lib.generateKeys(4096, 1);
        var aliceRsaPubKey = aliceRsaKeys.publicKeyBytes;
        var aliceAddress = new Address(aliceRsaPubKey);

        var bobRsaKeys = CryptoManager.lib.generateKeys(4096, 1);
        var bobRsaPubKey = bobRsaKeys.publicKeyBytes;
        var bobAddress = new Address(bobRsaPubKey);

        // === STEP 2: Bob shares his address with Alice, who initiates contact request ===
        Console.WriteLine("STEP 2: Alice -> Bob (requestAdd2)");

        Console.WriteLine($"  Alice sends RSA public key: {aliceRsaPubKey.Length} bytes");
        Console.WriteLine($"  Alice's address: {aliceAddress.ToString()}\n");

        // Alice signs requestAdd2 (her initial contact request containing her RSA public key)
        var aliceRequestAdd2Payload = Concat(bobAddress.addressNoChecksum, aliceRsaPubKey);
        var aliceRequestAdd2Signature = CryptoManager.lib.getSignature(aliceRequestAdd2Payload, aliceRsaKeys.privateKeyBytes);
        bool aliceRequestAdd2SigValidForBob = CryptoManager.lib.verifySignature(aliceRequestAdd2Payload, aliceRsaPubKey, aliceRequestAdd2Signature);
        Console.WriteLine($"  [✓] Alice signs requestAdd2 payload: {aliceRequestAdd2Signature.Length} bytes");
        Console.WriteLine($"      (Simulated) Bob verifies signature: {aliceRequestAdd2SigValidForBob}\n");

        // === STEP 3: Bob responds with acceptAdd2 ===
        Console.WriteLine("STEP 3: Bob -> Alice (acceptAdd2)\n");


        // Bob generates ephemeral ECDH keypair
        var bobEcdhKeypair = CryptoManager.lib.generateECDHKeyPair();
        Console.WriteLine($"  [✓] Bob generates ECDH keypair");
        Console.WriteLine($"      Public: {bobEcdhKeypair.publicKey.Length} bytes (secp521r1)");
        
        // Bob generates ML-KEM keypair (THIS IS THE POST-QUANTUM PART!)
        var bobMlKemKeypair = CryptoManager.lib.generateMLKemKeyPair();
        Console.WriteLine($"  [✓] Bob generates ML-KEM-1024 keypair (FIPS 203)");
        Console.WriteLine($"      Public: {bobMlKemKeypair.publicKey.Length} bytes");
        Console.WriteLine($"      Private: {bobMlKemKeypair.privateKey.Length} bytes");
        
        // Bob generates AES salt
        var bobAesSalt = CryptoManager.lib.getSecureRandomBytes(32);
        Console.WriteLine($"  [✓] Bob generates AES salt: 32 bytes\n");

        Console.WriteLine($"  Bob sends to Alice:");
        Console.WriteLine($"    - RSA public key: {bobRsaPubKey.Length} bytes");
        Console.WriteLine($"    - ECDH public key: {bobEcdhKeypair.publicKey.Length} bytes");
        Console.WriteLine($"    - ML-KEM public key: {bobMlKemKeypair.publicKey.Length} bytes");
        Console.WriteLine($"    - AES salt: {bobAesSalt.Length} bytes\n");

        var bobAcceptPayload = Concat(aliceAddress.addressNoChecksum, bobRsaPubKey, bobEcdhKeypair.publicKey, bobMlKemKeypair.publicKey, bobAesSalt);
        var bobAcceptSignature = CryptoManager.lib.getSignature(bobAcceptPayload, bobRsaKeys.privateKeyBytes);
        bool bobAcceptSigValidForAlice = CryptoManager.lib.verifySignature(bobAcceptPayload, bobRsaPubKey, bobAcceptSignature);
        Console.WriteLine($"  [✓] Bob signs acceptAdd2 payload: {bobAcceptSignature.Length} bytes");
        Console.WriteLine($"      Alice verifies signature: {bobAcceptSigValidForAlice}\n");

        // === STEP 4: Alice responds with keys2 ===
        Console.WriteLine("STEP 4: Alice -> Bob (keys2)\n");
        
        // Alice generates her own ECDH keypair
        var aliceEcdhKeypair = CryptoManager.lib.generateECDHKeyPair();
        Console.WriteLine($"  [✓] Alice generates ECDH keypair");
        
        // Alice performs ECDH key agreement with Bob's public key
        var ecdhSharedSecret = CryptoManager.lib.deriveECDHSharedKey(
            aliceEcdhKeypair.privateKey,
            bobEcdhKeypair.publicKey
        );
        Console.WriteLine($"  [✓] Alice derives ECDH shared secret: {ecdhSharedSecret.Length} bytes");
        
        // Alice encapsulates a secret using Bob's ML-KEM public key
        var mlKemResult = CryptoManager.lib.encapsulateMLKem(bobMlKemKeypair.publicKey);
        Console.WriteLine($"  [✓] Alice encapsulates ML-KEM secret");
        Console.WriteLine($"      Ciphertext: {mlKemResult.ciphertext.Length} bytes");
        Console.WriteLine($"      Shared secret: {mlKemResult.sharedSecret.Length} bytes");
        
        // Alice combines both secrets
        var aliceCombinedSecrets = new byte[ecdhSharedSecret.Length + mlKemResult.sharedSecret.Length];
        Buffer.BlockCopy(ecdhSharedSecret, 0, aliceCombinedSecrets, 0, ecdhSharedSecret.Length);
        Buffer.BlockCopy(mlKemResult.sharedSecret, 0, aliceCombinedSecrets, ecdhSharedSecret.Length, mlKemResult.sharedSecret.Length);
        Console.WriteLine($"  [✓] Alice combines secrets: {aliceCombinedSecrets.Length} bytes\n");
        
        // Alice generates ChaCha salt
        var aliceChachaSalt = CryptoManager.lib.getSecureRandomBytes(32);
        
        Console.WriteLine($"  Alice sends to Bob:");
        Console.WriteLine($"    - ECDH public key: {aliceEcdhKeypair.publicKey.Length} bytes");
        Console.WriteLine($"    - ML-KEM ciphertext: {mlKemResult.ciphertext.Length} bytes");
        Console.WriteLine($"    - ChaCha salt: {aliceChachaSalt.Length} bytes\n");

        var aliceKeys2Payload = Concat(bobAddress.addressNoChecksum, aliceEcdhKeypair.publicKey, mlKemResult.ciphertext, aliceChachaSalt, bobEcdhKeypair.publicKey, bobMlKemKeypair.publicKey, bobAesSalt);
        var aliceKeys2Signature = CryptoManager.lib.getSignature(aliceKeys2Payload, aliceRsaKeys.privateKeyBytes);
        bool aliceKeys2SigValidForBob = CryptoManager.lib.verifySignature(aliceKeys2Payload, aliceRsaPubKey, aliceKeys2Signature);
        Console.WriteLine($"  [✓] Alice signs keys2 payload: {aliceKeys2Signature.Length} bytes");
        Console.WriteLine($"      Bob verifies signature: {aliceKeys2SigValidForBob}\n");

        // === STEP 4: Bob decapsulates and derives keys ===
        Console.WriteLine("STEP 4: Bob derives session keys\n");
        
        // Bob performs ECDH with Alice's public key
        var bobEcdhShared = CryptoManager.lib.deriveECDHSharedKey(
            bobEcdhKeypair.privateKey,
            aliceEcdhKeypair.publicKey
        );
        Console.WriteLine($"  [✓] Bob derives ECDH shared secret: {bobEcdhShared.Length} bytes");
        
        // Bob decapsulates Alice's ML-KEM ciphertext
        var bobMlKemShared = CryptoManager.lib.decapsulateMLKem(
            bobMlKemKeypair.privateKey,
            mlKemResult.ciphertext
        );
        Console.WriteLine($"  [✓] Bob decapsulates ML-KEM secret: {bobMlKemShared.Length} bytes");
        
        // Bob combines secrets (should match Alice's)
        var bobCombinedSecrets = new byte[bobEcdhShared.Length + bobMlKemShared.Length];
        Buffer.BlockCopy(bobEcdhShared, 0, bobCombinedSecrets, 0, bobEcdhShared.Length);
        Buffer.BlockCopy(bobMlKemShared, 0, bobCombinedSecrets, bobEcdhShared.Length, bobMlKemShared.Length);
        
        // Verify both sides have the same combined secret
        bool secretsMatch = bobCombinedSecrets.SequenceEqual(aliceCombinedSecrets);
        Console.WriteLine($"  [✓] Secrets match: {secretsMatch}\n");

        // Both derive final session keys using HKDF
        var aliceAesKey = CryptoManager.lib.deriveSymmetricKey(aliceCombinedSecrets, 32, bobAesSalt, IXI_AES_KEY_INFO);
        var aliceChachaKey = CryptoManager.lib.deriveSymmetricKey(aliceCombinedSecrets, 32, aliceChachaSalt, IXI_CHACHA_KEY_INFO);
        
        var bobAesKey = CryptoManager.lib.deriveSymmetricKey(bobCombinedSecrets, 32, bobAesSalt, IXI_AES_KEY_INFO);
        var bobChachaKey = CryptoManager.lib.deriveSymmetricKey(bobCombinedSecrets, 32, aliceChachaSalt, IXI_CHACHA_KEY_INFO);
        
        Console.WriteLine("STEP 5: Derive final session keys\n");
        Console.WriteLine($"  [✓] AES-256 keys derived (32 bytes each)");
        Console.WriteLine($"      Alice AES key matches Bob: {aliceAesKey.SequenceEqual(bobAesKey)}");
        Console.WriteLine($"  [✓] ChaCha20 keys derived (32 bytes each)");
        Console.WriteLine($"      Alice ChaCha key matches Bob: {aliceChachaKey.SequenceEqual(bobChachaKey)}\n");

        Console.WriteLine("═══════════════════════════════════════════════════════\n");
        Console.WriteLine("Security Analysis:");
        Console.WriteLine("  [✓] ECDH (secp521r1)");
        Console.WriteLine("  [✓] ML-KEM-1024 (FIPS 203) - Quantum resistance");
        Console.WriteLine("  [✓] HKDF-SHA3-512 - Key derivation");
        Console.WriteLine("  [✓] Dual encryption - AES-256 + ChaCha20-Poly1305");
        Console.WriteLine("\n  -> Attacker needs to break ECDH AND ML-KEM");
        Console.WriteLine("  -> Even if ECDH is quantum-broken, ML-KEM protects");
        Console.WriteLine("  -> RSA signatures prevent man-in-the-middle");
        Console.WriteLine("\n═══════════════════════════════════════════════════════\n");

        // Store for encryption demo
        _aliceAesKey = aliceAesKey;
        _aliceChachaKey = aliceChachaKey;
        _bobAesKey = bobAesKey;
        _bobChachaKey = bobChachaKey;
    }

    static byte[] _aliceAesKey;
    static byte[] _aliceChachaKey;
    static byte[] _bobAesKey;
    static byte[] _bobChachaKey;

    static void DemoEncryption()
    {
        Console.WriteLine("## Message Encryption with Derived Keys\n");

        string message = "This message is protected by quantum-resistant crypto!";
        byte[] plaintext = Encoding.UTF8.GetBytes(message);
        byte[] aad = Encoding.UTF8.GetBytes("chat:timestamp:1234567890");

        Console.WriteLine($"Original message: \"{message}\"\n");

        // Encrypt using spixi2 mode (what Ixian actually uses)
        var encrypted = MessageCrypto.encrypt(
            StreamMessageEncryptionCode.spixi2,
            plaintext,
            null,  // No RSA for session messages
            _aliceAesKey,
            _aliceChachaKey,
            aad
        );

        Console.WriteLine($"Encrypted size: {encrypted.Length} bytes");
        Console.WriteLine($"  - Message nonce: 64 bytes");
        Console.WriteLine($"  - AES-256-GCM ciphertext");
        Console.WriteLine($"  - ChaCha20-Poly1305 outer encryption");
        Console.WriteLine($"  - Authentication tag verified\n");

        // Decrypt using Bob's keys (which match Alice's)
        var decrypted = MessageCrypto.decrypt(
            StreamMessageEncryptionCode.spixi2,
            encrypted,
            null,
            _bobAesKey,
            _bobChachaKey,
            aad
        );

        string decryptedMessage = Encoding.UTF8.GetString(decrypted);
        Console.WriteLine($"Decrypted message: \"{decryptedMessage}\"");
        Console.WriteLine($"✓ Integrity verified via Poly1305 + GCM\n");
    }
}
