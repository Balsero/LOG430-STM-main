# Laboratoire #1

Groupe: 0x

Equipe: 0x

Membres de l'équipe:

# Évaluation de la participation

> L'évaluation suivante est faite afin d'encourager des discussions au sein de l'équipe. Une discussion saine du travail de chacun est utile afin d'améliorer le climat de travail. Les membres de l'équipe ont le droit de retirer le nom d'un ou une collègue du rapport.
> |nom de l'étudiant| Facteur multiplicatif|
> |:---------------:|:--------------------:|
> |Jean Travaillant | 1 |
> |Joe Paresseux | 0.75 |
> |Jules Procrastinateu| 0.5 |
> |Jeanne Parasite | 0.25 |
> |Jay Oublié| 0 |

# Introduction /2

> TODO: insérer votre introduction

# Diagramme de séquence de haut niveau

## Explication générale du fonctionnement du système /2

> TODO: Résumer sommairement ce que chaque microservice fait dans le système :

- **NodeController** :
- **STM** :
- **RouteTimeProvider** :
- **TripComparator** :

## Diagramme de séquence /3

> TODO: Insérer le diagramme de séquence de haut niveau pour illustrer ce qui arrive lorsqu'une requête de comparaison de trajet provenant du dashboard arrive au système.

# Diagrammes de contexte de haut niveau

## Microservice STM /3

> TODO: Insérer le diagramme de contexte de haut niveau pour le microservice STM.

## Microservice RouteTimeProvider /3

> TODO: Insérer le diagramme de contexte de haut niveau pour le microservice RouteTimeProvider.

## Microservice TripComparator /3

> TODO: Insérer le diagramme de contexte de haut niveau pour le microservice TripComparator.

# Questions générales /12

1. L'ajout de la base de donnés est une amélioration au système. Quel attribut de qualité est amélioré par l'ajout d'une base de données? Expliquez votre réponse. **/3**

2. Lorsqu'une requête de comparaison du temps de trajet faite sur le Dashboard est envoyée au NodeController, différentes requêtes sont échangées sur le réseau. Pendant une minute, combien de requêtes sont échangées entre les microservices **NodeController**, **TripComparator**, **RouteTimeProvider** et **STM** et avec les API externes de la STM et de TomTom ? Décrivez également rapidement les différentes requêtes échangées. **/4**

2.1 Dans le code source du projet, nous pouvons ajuster le taux de lancement des requêtes. Expliquez comment nous pouvons nous y prendre pour ce faire en donnant un ou des extraits spécifiques de code. **/2**

3. Proposez une tactique permettant d'améliorer la disponibilité de l'application lors d'une attaque des conteneurs de computation (**TripComparator**, **RouteTimeProvider**, et **STM**) lors du 2e laboratoire. **/3**

# Conclusion du laboratoire /2

> TODO: insérer votre conclusion

- N'oubliez pas d'effacer les TODO
- Générer une version PDF de ce document pour votre remise finale.
- Assurez-vous du bon format de votre rapport PDF.
- Créer un tag git avec la commande "git tag laboratoire-1"