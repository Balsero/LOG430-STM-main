# Laboratoire #1

Groupe: 02

Equipe: 04

Membres de l'équipe: Ali Dickens Augustin, Jean-Philippe Lalonde, Jonathan Rodriguez Tames et Alexandre Roy

# Évaluation de la participation

|Nom de l'étudiant| Facteur multiplicatif|
|:---------------:|:--------------------:|
|Ali Dickens Augustin | 1 | 
|Jean-Philippe Lalonde | 1 |
|Jonathan Rodriguez Tames| 1 |
|Alexandre Roy | 1 |

# Introduction /2

Dans ce laboratoire, l'objectif principal est de comprendre et mettre en œuvre les concepts de microservice du projet LOG430-STM. De plus, chaque microservice joue un rôle distinct dans le but d'interagir afin de fournir des fonctionnalités telles que la collecte, le traitement et la comparaison de données liés au transport en commun des bus de la STM, et ce, en temps réel. L'architecture du projet implique entre autres une série de conteneurs Docker, qui doivent être déployés ainsi que configurés correctement dans le but de garantir la communication efficace entre les différents microservices. Ainsi, l'équipe est amenée à explorer ensemble diverses tâches, dont la configuration initiale du système, de l'exécution de microservices sur Docker, d'interagir avec des API externes et de manipuler des données réelles, et ce, en passant par l'optimisation des performances grâce à l'ajout de bases de données, l'ajout d'un ping echo et donc à la mise en œuvre de tactiques de résilience.

## Explication générale du fonctionnement du système /2

- **NodeController** : Ce dernier est considéré comme étant le chef d'orchestre des tests de chaos, assurant ainsi la perturbation contrôlée des microservices afin de tester entre autres leur résilience. Ensuite, celui-ci a pour but de surveiller la durée de vie des différents conteneurs Docker et de fournir des mécanismes pour gérer, équilibrer ou même créer la charge de ces derniers. Ainsi, le NodeController a pour but de faciliter la connexion aux serveurs de l'ÉTS ce qui permet donc de communiquer avec le restant du système et son API permet d'ajuster dynamiquement les différents services en cours d'exécution. Finalement, son équilibrage de charge utilise deux stratégies étant le Round Robin étant décrit comme distributeur de requêtes de manière aléatoire, et ce, parmi les instances disponibles. Le second étant Broadcast, qui lui envoie plutôt la requête à toutes les instances.
- **STM** : Le microservice STM agit de son côté comme étant un adaptateur pour l'API de la Société de Transport de Montréal (STM). Ce dernier fournit entre autres des fonctionnalités supplémentaires par rapport à l'API originale, dont l'obtention de données à intervalles réguliers (50 ms) et le suivi en temps réel des bus entre deux coordonnées spécifiques. Le STM est basé sur une architecture en couche, respectant donc le principe d'inversion des dépendances dans le but de faciliter toutes modifications et de maximiser la modularité. Ainsi, afin d'améliorer les performances de ce service, l'équipe doit donc configurer une base de données afin que celui-ci stocke ses données statiques GTFS dans cette dernière (PostgreSQL) plutôt que de la conserver en mémoire ce qui réduit alors l'utilisation de la RAM.
- **RouteTimeProvider** : Le microservice RouteTimeProvider a pour but de fournir des informations sur le temps de trajet basé sur les routes et les horaires disponibles pour les différents bus de la STM ou voitures, et ce, grâce à son utilisation de l'API TomTom. Au début, ce microservice n'est pas fonctionnel dans le projet et donc la création d'un fichier Dockerfile est obligatoire afin de rendre ce dernier opérationnel permettant ainsi la possibilité d'exécuter ses différentes fonctions. De plus, lorsque ce dernier configuré selon les normes du projet, il exposera une route nommée ping echo ayant pour but simple de vérifier son état de fonctionnement tous les 0.5 seconde, et ce, grâce à son instance. Si cette dernière est active, la route transmettra un message isAlive, permettant aux autres microservices de s'assurer qu'il est bien fonctionnel.
- **TripComparator** : Finalement, le TripComparator a pour mission de comparer les différentes options de trajet. Son fonctionnement est qu'il surveille en permanence l'état du RouteTimeProvider afin de vérifier sa disponibilité grâce à l'envoi du ping echo configuré préalablement. De plus, si le microservice RouteTimeProvider est redéployé ou bien détruit, ce dernier va détecter ce changement et donc identifier un nouveau port sur lequel l'instance sera possible. Ainsi, cette fonctionnalité de surveillance continue assure la réactivité et le fonctionnement opérationnel du système le rendant ainsi plus dynamique aux différents changements.

# Diagramme de séquence de haut niveau

## Diagramme de séquence /3

1. **Acteurs** :
    - Utilisateur
    - Dashboard
    - NodeController
    - TripComparator
    - STM
    - RouteTimeProvider
    - API STM
    - API TOMTOM
